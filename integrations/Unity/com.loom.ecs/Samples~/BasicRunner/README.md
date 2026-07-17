# Loom Basic Runner sample

1. Import this sample from Package Manager → Loom Unity → Samples → **Basic Runner**.
2. Create an empty GameObject, add `SampleGameRunner`.
3. Optional: assign a prefab with `EntityBehaviour` (or a bare Transform) to **Entity View Prefab**.
4. Enter Play Mode — entities orbit; open **Window → Loom → Entity Debugger** to inspect/edit them.

Register motion before `TransformSyncSystem` (as the sample does). Use `[UpdateBefore]` / `[UpdateAfter]` on your own system types when registration order is not enough.
