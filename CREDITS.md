# Credits

Third-party assets used in the game. Add a row for every new file taken from someone else.

## Sounds

### Chimes

All but the horn are from **Interface Sounds** by Kenney (<https://kenney.nl/assets/interface-sounds>),
released under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) (public domain,
credit appreciated but not required). The pack's license is kept in
`Art/Audio/Chimes/License.txt`.

| File (in `Assets/Audio/Chimes/`) | Used for (Chimes sound set) | Source          | Author | License |
|----------------------------------|-----------------------------|-----------------|--------|---------|
| `glass_001.ogg`                  | Match of 3                  | Interface Sounds | Kenney | CC0     |
| `confirmation_001.ogg`           | Match of 4                  | Interface Sounds | Kenney | CC0     |
| `confirmation_002.ogg`           | Match of 5+                 | Interface Sounds | Kenney | CC0     |
| `tick_002.ogg`                   | Countdown tick              | Interface Sounds | Kenney | CC0     |
| `drop_002.ogg`                   | Swap                        | Interface Sounds | Kenney | CC0     |
| `error_008.ogg`                  | Invalid swap                | Interface Sounds | Kenney | CC0     |

`horn.wav` (round-end horn) isn't third-party: it's synthesized by `Art/Audio/build_horn.py`.

### Arcade

None of these are third-party. They were made for this game with
[jsfxr](https://sfxr.me) (an 8-bit sound effect generator, itself public domain) and are
released under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) like the rest.
Each one's preset is kept in `Art/Audio/Arcade/`, and `Art/Audio/build_arcade.js` renders the
presets to the files below.

| File (in `Assets/Audio/Arcade/`) | Used for (Arcade sound set) | Source                    | Author      | License |
|----------------------------------|-----------------------------|---------------------------|-------------|---------|
| `match3.wav`                     | Match of 3                  | `match3.json` preset      | This project | CC0     |
| `match4.wav`                     | Match of 4                  | `match4.json` preset      | This project | CC0     |
| `match5_plus.wav`                | Match of 5+                 | `match5_plus.json` preset | This project | CC0     |
| `countdown_tick.wav`             | Countdown tick              | `countdown_tick.json` preset | This project | CC0  |
| `round_end_horn.wav`             | Round-end horn              | `round_end_horn.json` preset | This project | CC0  |
| `swap.wav`                       | Swap                        | `swap.json` preset        | This project | CC0     |
| `invalid_swap.wav`               | Invalid swap                | `invalid_swap.json` preset | This project | CC0    |
| `achievement.wav`                | Achievement unlocked        | `achievement.json` preset | This project | CC0     |
