# Genuine legacy save fixtures

Generated from the unmodified domain source at `2e16c95b22dd5707b47e130e38c6ce046ca12bc5` using its original `StableSaveCodec`, `SaveTestData.Initial()` and `SaveTestData.Commit().State` in an isolated scratch assembly. The initial fixture contains two known headless-test parents; the second contains their committed conception, unborn identity, ledger debit and capacity hold. TEST-ONLY release fixtures were used by that pre-existing conception test. These are not player save files or visual approvals.

[
  {
    "file": "old_conception_v1.clvr",
    "sha256": "206da70953a3637096a3bbeebe89adb64c31d624825a5d610e06995e80ea9fde",
    "bytes": 19132,
    "wireVersion": 1
  },
  {
    "file": "old_static_initial_v1.clvr",
    "sha256": "8ea6375fc5f7be460903cc4583f88241e75d551955003f7bf6a4de75d172db4c",
    "bytes": 4942,
    "wireVersion": 1
  }
]

## Genuine daily-care v2 fixtures

Generated using the unmodified domain source and original v2 codec at `b541bc470e78e1c52374096d1f158545c0d9c1ea` in an isolated scratch assembly. The existing v1 fixtures were decoded by that build and advanced with its original `AtomicSaveStore.CommitEndDay`: one ordinary day for the initial stable, three days ending immediately before birth for the committed pregnancy. The saved care values, costs, immutable receipt hashes, conception and all RNG streams are baseline evidence, not reconstructed new-format data.

```json
[
  {
    "file": "old_care_day_v2.clvr",
    "sha256": "34f190d6805fd2edb4bb4cee3ca5d71c50098bc101aa0e0c2909bdabf444e8a2",
    "bytes": 5731,
    "wireVersion": 2
  },
  {
    "file": "old_pregnancy_care_v2.clvr",
    "sha256": "189dcc6c749ffe57cadebedfe76eef1c7e2da42222a28b150967960d81386501",
    "bytes": 21182,
    "wireVersion": 2
  }
]
```

## Genuine v2 born-foal compatibility fixture

`old_born_foal_v2.clvr` was validated and encoded by the same unmodified `b541bc4` v2 domain/codec. It retains the genuine historical conception and offspring identity, with the previously supported born snapshot fields: actual birth day 14, Foal stage, age counter 4, cleared maternal pregnancy and recovery through day 16. The snapshot calendar is 14 and save revision 5. It has no daily or lifecycle completion receipts; the old format accepted this state. This fixture tests preservation of pre-existing birth data, not a claim that the former management screen could process births.

Size: 19307 bytes. Wire version: 2. SHA-256: `110227d2a9b6b7e1b56f626577f4f2f53f786a9f09e93b88098e52f737e0933b`.

## Genuine v2 nonaligned age fixture

`old_born_foal_unaligned_v2.clvr` was validated and encoded by the same original v2 source at `b541bc4`. It has the same historical born-foal conception, actual birth day 14 and calendar day 14, but its stored age counter is 19. The old format accepted this counter independently of the conception/calendar dates. This fixture verifies that migration captures and continues that old value, rather than accepting a save that fails on the next End Day. No genetics, parentage, conception or random streams were changed.

Size: 19307 bytes. Wire version: 2. SHA-256: `fa345b45c0e4064611400a95e74d3af83763042dae854e40e04fefdceb3c9f24`.
