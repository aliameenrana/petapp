const GAME_TIME = 30;
const DROP_EVERY = 0.85;
const GHOST_CHANCE = 0.2;
const LUCKY_CHANCE = 0.15;
const LANE = 2.6;
const SPAWN_Y = 7.5;
const PET_Y = -4.25;
const GHOST_PENALTY = 3;
const LUCKY_POINTS = 5;

let score = 0;
let combo = 0;

// Restricts v to the [min, max] range.
function clamp(v: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, v));
}

// Speeds up with time rather than score, so a good run isn't punished with harder drops.
function fallSpeed(): number {
  return 3 + sdk.time * 0.12;
}

// Host-called entry point: sets up the stage, camera, pet, input and the game's timers.
function onStart(): void {
  sdk.camera.use("side2d"); // Switches to the fixed side-on 2D camera rig for this game.
  sdk.audio.music("music_game"); // Crossfades from the home loop into this game's music track.
  sdk.spawn("bg", { x: 0, y: 0 }); // Spawns the background prefab behind the play area.
  sdk.pet.x = 0; // Centers the pet horizontally at the start of the round.
  sdk.pet.y = PET_Y; // Places the pet on its catch line near the bottom of the stage.

  sdk.input.onDrag(p => {
    sdk.pet.x = clamp(p.worldX, -LANE, LANE); // Follows the drag horizontally, kept within the lane bounds.
  });

  sdk.ui.banner("Catch the coins!"); // Shows the opening instruction banner.
  sdk.ui.showTimer(GAME_TIME); // Starts the on-screen countdown display.
  sdk.timer.every(DROP_EVERY, dropSomething); // Spawns a new falling item on a fixed interval.
  sdk.timer.after(GAME_TIME - 5, () => sdk.ui.banner("5 seconds!")); // Warns the player 5 seconds before time runs out.
  sdk.timer.after(GAME_TIME, () => sdk.endGame({ score })); // Ends the session and reports the final score once time is up.
}

// Spawns a short-lived particle effect above the pet and cleans it up after it plays out.
function burst(key: "sparkle" | "poof"): void {
  const fx = sdk.spawn(key, { x: sdk.pet.x, y: PET_Y + 0.8 }); // Spawns the effect at the pet's position, slightly above it.
  sdk.timer.after(1.2, () => fx.destroy()); // Removes the effect object once its animation has had time to finish.
}

// Spawns one falling item (coin, lucky or ghost) and wires up what happens when it lands or is missed.
function dropSomething(): void {
  // Fair finish: never drop something that can't reach the pet before time runs out.
  const fallTime = (SPAWN_Y - PET_Y) / fallSpeed(); // Time this item would take to fall from spawn height to the catch line.
  if (sdk.time + fallTime > GAME_TIME - 0.2) return; // Skips the drop if it couldn't land before the round ends.

  const roll = sdk.random(); // Single roll of the dice decides which item kind this drop is.
  const kind = roll < GHOST_CHANCE ? "ghost" : roll < GHOST_CHANCE + LUCKY_CHANCE ? "lucky" : "coin"; // Weighted pick: ghost, then lucky, then coin as the default.
  const item = sdk.spawn(kind, { x: sdk.random(-LANE, LANE), y: SPAWN_Y }); // Spawns the item at a random lane position, above the visible stage.
  item.velocity = { x: 0, y: -fallSpeed() * (kind === "lucky" ? 0.8 : 1) }; // Falls straight down; lucky items drift slower so they're easier to grab.

  item.onTouch(sdk.pet, () => { // Fires once when the item reaches the pet.
    item.destroy(); // Removes the item immediately on contact.
    if (kind === "ghost") {
      combo = 0; // Ghosts break the current combo streak.
      score = Math.max(0, score - GHOST_PENALTY); // Deducts the penalty, never dropping the score below zero.
      sdk.pet.play("sad"); // Plays the pet's sad reaction animation.
      sdk.audio.play("sfx_bomb"); // Plays the ghost-hit sound effect.
      sdk.camera.shake(0.6); // Shakes the camera hard to sell the hit.
      burst("poof"); // Spawns the poof particle effect at the pet.
    } else if (kind === "lucky") {
      combo += 1; // Lucky catches extend the combo streak too.
      score += LUCKY_POINTS; // Awards the flat lucky-item bonus.
      sdk.pet.play("happy"); // Plays the pet's happy reaction animation.
      sdk.audio.play("sfx_lucky"); // Plays the lucky-catch sound effect.
      sdk.camera.shake(0.25); // Small camera shake for a lighter hit than the ghost.
      sdk.ui.banner(`Lucky! +${LUCKY_POINTS}`); // Announces the bonus points on screen.
      burst("sparkle"); // Spawns the sparkle particle effect at the pet.
    } else {
      combo += 1; // Regular coin catches build the combo streak.
      // Every 5 in a row adds a point per coin.
      score += 1 + Math.floor(combo / 5); // Base point per coin, plus a growing bonus the longer the streak runs.
      sdk.pet.play("happy"); // Plays the pet's happy reaction animation.
      sdk.audio.play(combo % 5 === 0 ? "sfx_lucky" : "sfx_catch"); // Uses the special sound only on every 5th catch in the streak.
      if (combo % 5 === 0) sdk.ui.banner(`${combo} combo!`); // Calls out the milestone every 5 catches.
      burst("sparkle"); // Spawns the sparkle particle effect at the pet.
    }
    sdk.ui.setScore(score); // Pushes the updated score to the HUD.
  });

  item.onOffscreen(() => { // Fires once if the item falls past the stage without being caught.
    item.destroy(); // Cleans up the missed item.
    if (kind !== "ghost") combo = 0; // Missing a coin or lucky item breaks the combo; a missed ghost does not.
  });
}
