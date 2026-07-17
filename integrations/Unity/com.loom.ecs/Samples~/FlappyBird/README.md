# Loom Flappy Bird sample

Playable Flappy Bird–style demo on Loom ECS (systems, `CommandBuffer`, events, singletons).

## Setup

1. Import from Package Manager → Loom → Samples → **Flappy Bird**.
2. Open `FlappyBird.unity`, or create an empty GameObject and add `FlappyBirdRunner`
   (pulls in `FlappyBirdIndirectRenderer` + Camera).
3. Enter Play Mode (Game view).

### Art (project textures)

All visuals come from PNGs under `Resources/FlappyBird/`:

| Asset | Role |
|-------|------|
| `sky.png` | Full playfield backdrop |
| `hills.png` | Parallax silhouette above the ground |
| `cloud.png` | Soft cloud sprites |
| `ground.png` | Horizontally tiled scrolling ground |
| `bird.png` | Player bird (rotated by velocity) |
| `pipe_body.png` | Vertically tiled pipe column |
| `pipe_cap.png` | Pipe lip at the gap |
| `white.png` | Tinted overlays (death dim) |

Assign them on `FlappyBirdIndirectRenderer`, or leave empty to load via `Resources.Load` after the sample is imported. Shader: `Loom/FlappyBirdEnvironment`.

HUD text stays on OnGUI. Optional: **Window → Loom → Entity Debugger**.

## Controls

| Input | Action |
|-------|--------|
| Space / Click / ↑ | Flap (also starts the round) |
| R or Space / Click after death | Retry |

Logical playfield is 400×600 (letterboxed to the Game view).
