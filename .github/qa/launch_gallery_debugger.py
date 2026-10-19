"""Launch our test as gdb's child; inspect a stall without relaxing ptrace policy.

The caller retains the original 180-second process-group timeout. This controller
never turns a debugger error or a killed inferior into a successful test result.
"""
import os
import re
import selectors
import subprocess
import sys
import time


def main():
    process = subprocess.Popen(
        ["gdb", "--quiet", "--nx", "--interpreter=mi2", "--args", *sys.argv[1:]],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        bufsize=0,
    )
    selector = selectors.DefaultSelector()
    selector.register(process.stdout, selectors.EVENT_READ)

    def send(command):
        process.stdin.write((command + "\n").encode())
        process.stdin.flush()

    send("1-gdb-set pagination off")
    send("2-gdb-set mi-async on")
    # Preserve ASLR: no runner security settings or ptrace restrictions are changed.
    send("3-gdb-set disable-randomization off")
    send("4-exec-run")
    buffer = ""
    ok_at = None
    scheduled_interrupt_sent = False
    tracing = False
    result = None
    try:
        while result is None:
            for key, _ in selector.select(timeout=0.5):
                data = os.read(key.fd, 65536)
                if not data:
                    return 1  # Debugger disappeared without a verified inferior exit.
                text = data.decode("utf-8", errors="replace")
                print(text, end="", flush=True)
                buffer += text
                while "\n" in buffer:
                    line, buffer = buffer.split("\n", 1)
                    if "[portrait-gallery] OK" in line and ok_at is None:
                        ok_at = time.monotonic()
                    if line.startswith('*stopped,reason="exited-normally"'):
                        result = 0
                    elif line.startswith('*stopped,reason="exited"'):
                        match = re.search(r'exit-code="([0-7]+)"', line)
                        result = int(match.group(1), 8) if match else 1
                    elif line.startswith('*stopped,reason="exited-signalled"'):
                        result = 1
                    elif line.startswith("*stopped"):
                        # Also inspect unexpected debugger stops; never swallow them.
                        send('6-interpreter-exec console "thread apply all bt 30"')
                        tracing = True
                    elif tracing and line.startswith("6^done"):
                        tracing = False
                        send("7-exec-continue")
                    elif re.match(r"[1-7]\^error", line):
                        return 1
            if ok_at is not None and not scheduled_interrupt_sent and time.monotonic() - ok_at >= 15:
                scheduled_interrupt_sent = True
                send("5-exec-interrupt --all")
            if process.poll() is not None and result is None:
                return 1
        return result
    finally:
        selector.close()
        if process.poll() is None:
            try:
                send("8-gdb-exit")
                process.wait(timeout=5)
            except (BrokenPipeError, subprocess.TimeoutExpired):
                process.terminate()


if __name__ == "__main__":
    sys.exit(main())
