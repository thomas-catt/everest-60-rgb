# RGB status bar (for mountain everest 60)
allows the bottom perimeter LEDs of the keyboard to be used as a status bar for various system values (volume, battery, cpu usage, etc.)

this is a proof of concept repository based on the findings in https://github.com/ramisotti13-eng/BaseCamp-Linux. **the project is entirely vibecoded**, not a single line of code was written by me.

### how it works
the app uses a uni-directional HID communication channel to send RGB updates. the specific signals to send were found in the aforementioned repository, which is a linux implementation of base camp (the official RGB control software for mountain devices).

because of how this keyboard behaves, there are some limitations introduced because of it:
- the everest 60 does not allow reading RGB map values saved onto its memory, so while you use this application, the keyboard's own RGB control would be frozen.
- another limitation is that this software does all its work under the keyboard's custom RGB mode. means you can't use RGB effects while this application is running
- sending feature update payload over HID freezes the keyboard's controller momentarily, so repeated RGB updates introduce keyboard's own microfreezes observable while typing at a moderate speed.

### usage

- **via releases:** open the built exe present in the releases.
- **from source:** this project is built on vs 2026 18.9. just clone, build and run.

### contribution
too lazy :\
