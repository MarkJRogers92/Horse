# D-042 — Hands-on grooming

**Date:** 2026-10-14
**Status:** Owner-approved local implementation; no push or merge before review.

## Decision

Add an optional “Groom by hand” activity beside the existing Pat, Brush and Carrot quick-care buttons. A private 3D presenter shows the selected horse with its real coat and three removable dirt patches. Dragging, tapping, or keyboard-accessible brush buttons clean the same bounded patches. Holding a pointer still does not add strokes.

The activity is presentation-only until Finish. It uses the existing once-per-day atomic `CommitInteraction(..., "brush", ...)` command and its existing bond gain. There is no extra reward, daily upkeep change, new save field, currency charge, or change to the optional daily-care grooming toggle. Quick Brush and the activity share the same daily eligibility.

Cancel or Escape discards unfinished progress without saving. Reopening starts a fresh session. Finish is latched before saving, disabled during an in-flight save, and rechecks day and eligibility. If a write response fails, the UI reloads the authoritative save: an existing brush marker for the same day confirms success, an unchanged eligible session can retry, and an expired session closes. No additional brush is applied to infer success. Closing is disabled while saving.

The private presenter temporarily sets tack aside so rugs do not obscure patches. This does not modify the horse's saved tack. Patches are placed from the existing skinned body map and fixed camera; the coat, source mesh, UVs and skeleton remain unchanged. Adult and foal appearances use the same path.

## Verification

`tools/grooming/GroomingSessionProbe.csproj` exercises bounds, stationary input, cancel/reopen, completeness, eligibility and save latching/retry. `--stable-life-capture=DIR` exercises the real UI, pointer tap, accessible brush buttons, cancel without saving, exactly one finished brush, and the existing quick-care path; it saves dirty and clean screenshots. Domain interaction tests continue to cover canonical bond persistence. Final evidence and limits are recorded in `STATUS.md`.
