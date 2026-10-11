# Playtest the stable day

Open the game and choose **Manage stable**. This prototype starts a new management slot with two adult horses, Juniper and Maple, and 500 coins. It uses the shared domain save format and store. Opening this screen again loads the same slot.

Feed and fresh bedding are included for each locally owned horse in the active stable. Choose optional turnout or grooming, then select **Review day**. The review shows the day's care, upkeep, any assistance, and the wallet balance after payment. **Keep planning** returns without advancing time. **End day** saves the care, payment, and one calendar day together.

Two active horses cost 36 coins per day: 6 for each horse and 24 for the stable. Optional turnout and grooming do not add a fee in this prototype. If ordinary care costs exceed the wallet, assistance covers the difference without granting spendable coins or creating debt. A horse cannot be lost to this upkeep system.

Each season has 28 days. Time advances only when you confirm End day; closing the game does not advance time. **Reload save** checks the saved state. An interrupted save keeps the exact pending request available through **Retry saved day**, which cannot charge for the same completed day twice. A verified backup requires an explicit restore before further changes.

The management slot is `user://stable-management/stable.clvr`; verification runs use a separate temporary path. This is a sandbox playtest and does not enable the unreleased breeding/visual catalog. Care currently has four simple scores: nutrition, bedding, turnout, and grooming. It does not yet affect training, fatigue, racing, or breeding.

Births and juvenile growth are later work. The day preview explains why it cannot advance across one of those boundaries, rather than skipping a due birth or changing a horse's identity. Horse models, coats, and animation development are outside this package.
