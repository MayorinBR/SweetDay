# Sweet Day Clone

A Unity-based game project inspired by the "Sweet Day" minigame from Nintendo Land. The goal is to create an asymmetrical experience where players collect coins while evading guards.

## Game Overview

In "Sweet Day Clone," players are divided into Runners and Catchers. Runners must collect coins to win, but the weight of the coins makes them slower and more visible. Catchers must work together to corner and hit the Runners before the score goal is reached or time runs out.
## Implemented Features

* **Asymmetrical Movement:** Distinct movement systems for Runners (fast) and Catchers (strategic) with physics and gravity support.
* **Dynamic Weight and Scaling System:** The Runner's backpack grows visually and reduces movement speed proportionally to the amount of coins carried.
* **Coin Interaction:** Mechanics for collecting, carrying, and dropping coins.
* **Dash Mechanic:** Runners can use the dash ability when the cooldown is avaliable.
* **Dynamic Button Spawners:** Interactive zones where players must stay for a set time to spawn new coins on the map.
* **Combat and Shared Life System:** Catchers feature an attack system with animated hitboxes. Damage reduces the Runners' global team lives.
* **Networking and Synchronization:** Fully implemented via Unity Netcode for GameObjects, ensuring score, timer, and positions are synchronized through NetworkVariables and RPCs.
* **Lobby and Relay System:** Simplified online connection using randomly generated codes with confused-character filtering.
* **Cross-Platform & Mobile UI:** Adaptable interface with virtual joysticks and specific buttons that auto-adjust based on the player’s role.
* **Session Management:** Game Over system with results screen and Restart functionality (Host only).
* **Scene Management:** At the main screen, player can choose different scene to allow different game levels.

## Planned Features (Roadmap)

* **Local Multiplayer (Split-Screen):** Support for up to 4 players on the same device with split-screen for Runners.
* **Power-Up System:** Random map items providing invisibility, coin magnetism, speed boosts...
* **Catcher Abilities:** Invisible traps with a placement limit (max 3) and cooldowns.
* **Alternative Modes:** "Master Collector" and "Escape Gate" alternative modes.
* **Interactive Gimmicks:** Some objects in the scene can be interracted with, allowing a more strategic and diverse gameplay.
* **Customization:** Player visual customization includinding accessories and skin color variants.
* **Advanced Audio System:** Audio Mixer for BGM and SFX.
