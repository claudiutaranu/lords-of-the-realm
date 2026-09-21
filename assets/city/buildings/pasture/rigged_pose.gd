class_name RiggedPose
extends Resource
## A painted pose plus the joints the rig shader bends. Positions are in texture pixels.

## Body layer (tail removed and the fur behind it rebuilt when a tail layer exists).
@export var texture: Texture2D
## G channel: how much each body pixel follows the head.
@export var rig_weights: Texture2D
## Optional separate tail layer; without it the tail stays as painted (e.g. back views where it covers a leg).
@export var tail_texture: Texture2D
## R channel: how much each tail pixel follows the swish (0 at the root).
@export var tail_rig_weights: Texture2D
@export var foot_point: Vector2
@export var tail_pivot: Vector2
@export var head_pivot: Vector2
## Direction the painting faces: decides which way a grazing head "dips".
@export var faces_right: bool = false
## Grazing heads "tear grass" in short tugs; others look around slowly.
@export var is_grazing: bool = false
