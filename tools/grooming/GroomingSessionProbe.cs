using System;
using System.Reflection;

// Reflection lets the first RED run assert that the feature does not exist yet,
// rather than fail to compile. After that these exercise the real presentation model.
var type = Assembly.GetExecutingAssembly().GetType("Cloverbrook.Game.UI.GroomingSession");
Check(type != null, "grooming session exists");
object New() => Activator.CreateInstance(type!, new object[] { new[] { (0.25f, 0.5f), (0.5f, 0.5f), (0.75f, 0.5f) }, 0.08f, 0.14f })!;
object? Call(object o, string method, params object[] args) => type!.GetMethod(method)!.Invoke(o, args);
T Read<T>(object o, string name) => (T)type!.GetProperty(name)!.GetValue(o)!;
var session = New();
Check(Read<int>(session, "CleanPatches") == 0 && !Read<bool>(session, "Complete"), "starts dirty and incomplete");
Check(!(bool)Call(session, "TryBeginFinish", true)!, "incomplete session cannot finish");
foreach (var p in new[] { (-1f, .5f), (1.1f, .5f), (.25f, 0f), (float.NaN, .5f), (.25f, float.PositiveInfinity) })
    Check((int)Call(session, "Dab", p.Item1, p.Item2)! == -1, "outside and non-finite input ignored");
Call(session, "BeginStroke", 0.25f, 0.5f);
int initial = Read<int>(session, "TotalStrokes");
for (int i = 0; i < 100; i++) Call(session, "DragTo", .25f, .5f);
Check(Read<int>(session, "TotalStrokes") == initial, "holding still does not brush");
Call(session, "EndStroke");
Call(session, "DragTo", .5f, .5f);
Check(Read<int>(session, "TotalStrokes") == initial, "drag without held button does nothing");
Call(session, "Cancel");
Call(session, "BrushPatch", 0);
Check(Read<int>(session, "TotalStrokes") == initial && !(bool)Call(session, "TryBeginFinish", true)!, "cancel is inert and cannot finish");
var reopened = New();
Check(Read<int>(reopened, "TotalStrokes") == 0, "reopen resets presentation-only dirt");
for (int p = 0; p < 3; p++) for (int i = 0; i < 8; i++) Call(reopened, "BrushPatch", p);
Check(Read<bool>(reopened, "Complete") && Read<int>(reopened, "CleanPatches") == 3, "all three patches must be clean");
int total = Read<int>(reopened, "TotalStrokes");
Call(reopened, "BrushPatch", 0);
Check(Read<int>(reopened, "TotalStrokes") == total, "excess strokes cannot exceed completion");
Check(!(bool)Call(reopened, "TryBeginFinish", false)!, "expired daily eligibility cannot finish");
Check((bool)Call(reopened, "TryBeginFinish", true)!, "eligible complete session can finish");
Check(!(bool)Call(reopened, "TryBeginFinish", true)!, "double finish latched while saving");
Call(reopened, "ResolveFinish", false);
Check((bool)Call(reopened, "TryBeginFinish", true)!, "failed save can be retried");
Call(reopened, "ResolveFinish", true);
Check(!(bool)Call(reopened, "TryBeginFinish", true)!, "saved session cannot finish twice");
Console.WriteLine("[grooming-session] PASS bounds, stationary input, cancel/reopen, completion, stale eligibility, save latch/retry");
static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }
