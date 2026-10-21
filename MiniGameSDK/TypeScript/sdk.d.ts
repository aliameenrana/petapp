/** A 2D point or direction, in world units. */
interface Vec2 { x: number; y: number; }
/** The world position of a tap or drag event. */
interface PointerInfo { worldX: number; worldY: number; }

interface GameObjectHandle {
  /** Unique id for this spawned object, stable for its lifetime. */
  readonly id: number;
  /** World X position; writable to move the object directly. */
  x: number;
  /** World Y position; writable to move the object directly. */
  y: number;
  /** Current velocity in world units per second. */
  velocity: Vec2;
  /** Triggers an animation clip on the object, if it has one. */
  play(clip: "jump" | "happy" | "sad"): void;
  /** Removes the object from the stage immediately. */
  destroy(): void;
  /** Calls fn once the first time this object overlaps the given other object. */
  onTouch(other: GameObjectHandle, fn: () => void): void;
  /** Calls fn once when this object leaves the visible stage bounds. */
  onOffscreen(fn: () => void): void;
}

interface Sdk {
  /** The handle for the app's own pet, already placed on the stage. */
  readonly pet: GameObjectHandle;
  /** Seconds since the game started. */
  readonly time: number;
  /** Spawns a prefab from the game's assets at the given position and returns its handle. */
  spawn(key: AssetKey, pos: Vec2): GameObjectHandle;
  /** Returns a random float in [0, 1). */
  random(): number;
  /** Returns a random float in [min, max). */
  random(min: number, max: number): number;
  readonly timer: {
    /** Calls fn once after the given number of seconds have elapsed. */
    after(seconds: number, fn: () => void): number;
    /** Calls fn repeatedly, once every given number of seconds. */
    every(seconds: number, fn: () => void): number;
    /** Stops a pending after/every timer by the id it returned. */
    cancel(id: number): void;
  };
  readonly input: {
    /** Calls fn once for each tap on the stage. */
    onTap(fn: (p: PointerInfo) => void): void;
    /** Calls fn continuously while a drag gesture is in progress. */
    onDrag(fn: (p: PointerInfo) => void): void;
  };
  readonly ui: {
    /** Updates the score label in the game HUD. */
    setScore(n: number): void;
    /** Starts the HUD countdown display for the given duration in seconds. */
    showTimer(seconds: number): void;
    /** Shows a short-lived text banner over the stage. */
    banner(text: string): void;
  };
  readonly audio: {
    /** Plays a one-shot sound effect from the game's assets. */
    play(key: AssetKey): void;
    /** Loops a track from the package in place of the app's music until the game ends. */
    music(key: AssetKey): void;
  };
  readonly camera: {
    /** Switches to a named camera rig for this game. */
    use(preset: "side2d"): void;
    /** Shakes the camera briefly; strength is clamped to [0, 1]. */
    shake(strength: number): void;
  };
  /** Logs values to the host console for debugging. */
  log(...values: unknown[]): void;
  /** Ends the session and reports the final result; only the first call per session counts. */
  endGame(result: { score: number }): void;
}

declare const sdk: Sdk;
