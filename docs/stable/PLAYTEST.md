# Playtest the stable day

Open the game and choose **Manage stable**. A new management slot starts with mature Juniper (mare), Maple (stallion), and 500 coins. Opening this screen again loads the same saved stable. Existing original starter saves keep their horses, names, genetics, care, wallet and calendar while a one-time compatibility upgrade enables the pair.

In **Breeding**, choose the saved mare and stallion, then **Review pairing**. Check the 50-coin fee, remaining wallet, four-day due date, ancestry warning and space reserved through adulthood. **Cancel** returns without charging, drawing a foal or reserving space. **Confirm · 50 coins** saves the pregnancy and the same permanent child together with its fee and future home allocation. Reload or reopen the game to check the pregnancy remains.

This prototype protects four eventual home places. Each owned horse, including an unborn or growing foal, already uses one; an existing child's saved reservation is not counted twice. Pairing needs a remaining place and 50 coins. A pregnant or recovering mare, a horse below breeding age, a recorded close relative, incompatible save data or a full stable has a visible blocked review. Unknown starter ancestry is shown as a warning; the game does not claim the pair is proved unrelated or reveal hidden genetic results.

End four days to meet the foal in this same normal save. Name **New foal** in the roster, reload, then continue to youngster after three more days and adulthood after six. Breeding age is birth +8 and mare recovery ends at birth +2. An uncertain conception save retains **Retry saved breeding**, which uses the exact command and cannot create a second foal or fee. Other save-changing controls pause until that result is resolved. The separately saved **Foaling demo** remains an optional example.

Feed and fresh bedding are included for each locally owned horse in the active stable. Choose optional turnout or grooming, then select **Review day**. The review shows the day's care, upkeep, any assistance, and the wallet balance after payment. **Keep planning** returns without advancing time. **End day** saves the care, payment, and one calendar day together.

Two active horses cost 36 coins per day: 6 for each horse and 24 for the stable. Optional turnout and grooming do not add a fee in this prototype. If ordinary care costs exceed the wallet, assistance covers the difference without granting spendable coins or creating debt. A horse cannot be lost to this upkeep system.

Each season has 28 days. Time advances only when you confirm End day; closing the game does not advance time. **Reload save** checks the saved state. An interrupted save keeps the exact pending request available through **Retry saved day**, which cannot charge for the same completed day twice. A verified backup requires an explicit restore before further changes.

The management slot is `user://stable-management/stable.clvr`; verification runs use a separate temporary path. Saved pairing uses D-006's explicit text-only Sandbox policy, with canonical inheritance and supported genetic closure. Production visual approvals remain unchanged. Care currently has four simple scores: nutrition, bedding, turnout, and grooming. It does not yet affect training, fatigue, racing, or breeding eligibility.

Choose **Foaling demo** to try a previously committed pregnancy in a separate saved stable. This leaves the normal management slot intact. The example begins with Juniper expecting a foal in four completed days, Maple as the other recorded parent, and 450 coins after the historical conception fee. Reopening the demo continues its saved progress.

Review and end four days. The birth-day review describes the expected arrival; confirming it activates the same saved offspring and records its parents and actual birth day. The newborn appears in the roster as **New foal**. Use its name control to save a name, then **Reload save** to check that the name, family and birth remain. The birth is part of the daily save, so retrying that day cannot produce another foal or charge another conception fee.

Juniper recovers for two days after birth. The foal becomes a young horse after three days, an adult after six, and reaches breeding age after eight, subject to the usual eligibility checks. Each roster card shows the next milestone; the day's review and saved history explain stage changes. These are compressed game timings.

The care plan applies to horses already born at the start of the day. Birth day still costs 36 coins for the two adults; the following days cost 42 coins for three active horses. Club assistance continues if funds run short. The family capacity commitment was saved at conception, so the stage changes use that existing obligation rather than reserving another slot.

The demo slot is `user://stable-management/foaling-demo.clvr`. It is headless-test-derived sandbox data, not a released breeding catalog or a complete breeding planner. Missing birth anchors or incompatible saved capacity evidence remain visible blocks. Horse models, coats and animations are unchanged; this screen presents lifecycle information as text.
