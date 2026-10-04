# Showcase song prompt

Paste-ready prompt for a generated soundtrack to the Desktop IDE reel.
The reel order is `scripts/showcase/playlist.json`. Record the reel with
`scripts/showcase-video.ps1`, which muxes `scripts/showcase/showcaseTrack1.mp3`.
For a different file, pass it to `scripts/record-showcase.ps1 -Audio`.
No music is bundled in the repository. Use a track you have rights to use.
The video is trimmed to whichever of the picture and the track is shorter.

Tests of generated tracks settled on a natural female voice, a calm
atmosphere, and soft melodic music. Whisper, breathy close-mic, and
sensual delivery were rejected, as were vocoder and robotic delivery.

Each cue has `startMs`, milliseconds from the splash. That cue stays on
screen until the next cue's `startMs`. `endMs` is the end of the reel.
The last `closingMs` milliseconds return to the splash for the outro line.
The recorder trims its lead-in so the video file starts on that same
clock, then muxes `-Audio` from the start of the file. Put a lyric on a
section by setting that cue's `startMs` to the lyric's time in the song.
`malda scripts/showcase/timeline_editor.malda` opens sliders for those
times at http://localhost:8094/. The page keeps this prompt on screen.
Section lengths follow the sliders. Earlier and Later reorder the example
browser, the manual pages, and the scenes inside their own tour. Each
scene has its dream lyric. Hear plays from that cue for the number of
seconds set on the page, starting at 3. Edit the prompt on the page.
Save writes the playlist and this prompt.
A scene still compiles at the beginning of its slot; the slot ends on
the next `startMs`, and a later cue waits for its own `startMs` when the
reel is ahead of the clock.

If the generator has a style box and a lyrics box, split on the headings
below. If it has one box, paste the style first. Bracketed lines are
directions and should not be sung.

## Style

```
80 BPM, soft dream-pop, airy and melodic. Natural female voice, clear, medium distance, even tone, light vibrato, long legato phrases, a quiet harmony in the distance. Calm atmosphere: wide pads, soft piano, felt electric piano, quiet synth pulse, light reverb and delay. Neutral, steady, hopeful, moderate volume, even dynamics. No whisper, no breathy close-mic, no sensual or intimate delivery, no ASMR, no vocoder, no robotic voice, no spoken checklist, no rap, no belting, no drums beyond a soft pulse, no drop, no distortion. About 3 minutes. End by letting the last note fade into a resolved pad.
```

## Lyrics

```
[Intro – 18 seconds, pads only, then one clear line]
[Female voice, clear, medium distance, even tone]
Malda, let's begin.

[Catalog – 26 seconds, one continuous melody, four legato lines, steady and unhurried, do not accent every word]
[Female voice, clear and even]
Hello, confirm. The schema settles. Objects come alive.
The tower moves. The network answers. A page is built.
The set opens. Light returns. The ground is solid.
Bricks will fall. The pole stays up. This lap is ours. The king is watched.

[Manual – 24 seconds, a little wider, still even, the middle line is the refrain]
[Female voice, clear and even]
Open the book. The types lock in. Call the function.
Many agents, one objective.
Play it in the browser.

[Dream – 67 seconds, same tempo, clear even voice, hold each line, leave a short instrumental pause after every line]
[Female voice]
Printed clean.
The schema holds.
Three pegs, and done.
Every control answers.
Deeper. The edge holds.
The rays come home.
Cue down. The pocket calls.
Jump. A coin. Still running.
The combo climbs. The board clears.
The cart learns. The pole stands.
Apex. The corner. Through.
Last move. Objective done.

[Outro – 5 seconds, the splash returns, pads, one fading line]
[Female voice, clear, fading]
Malda.
```

## What each line follows

Catalog, in browse order: Hello, Schemas, Objects, Hanoi, XOR net, UI controls, Mandelbrot, Raytracer, Platform, Maldanoid, CartPole, Grand Prix, Chess.

Manual: the manual home page, data types, functions, neural nets
(“Many agents, one objective” is the multi-agent page), browser games.

Dream, in scene order: Hello, Schemas, Towers of Hanoi, UI controls, Mandelbrot, GPU raytracer, GPU billiards, Platform, Maldanoid, CartPole, Grand Prix, Chess.

## Saving a track the browser only plays

Prefer the generator’s own download. When the page only plays the song,
record the sound card with the same ffmpeg build the reel recorder uses
(`winget install Gyan.FFmpeg`). That build has no WASAPI loopback, so
capture goes through Stereo Mix.

Win+R, `mmsys.cpl`, Recording tab. Right-click an empty spot, show
disabled devices, and enable Stereo Mix. List capture devices:

```bat
ffmpeg -list_devices true -f dshow -i dummy
```

`Error opening input file dummy` is normal; the device names are printed
above it. A microphone entry records the room, not the browser. On this
machine the mix device is `Stereo Mix (Realtek(R) Audio)`. Leave
`C:\Windows\System32` so the file is writable, start playback, then:

```bat
cd /d "%USERPROFILE%\Documents"
ffmpeg -f dshow -i audio="Stereo Mix (Realtek(R) Audio)" -t 120 track.mp3
```

`-t 120` records two minutes. Press `q` to stop sooner. The browser must
be playing through that Realtek output. Bluetooth or another device
leaves the mix silent.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\showcase-video.ps1
```

That writes `artifacts/showcase/malda-showcase.mp4` with `scripts/showcase/showcaseTrack1.mp3`. Add `-SkipBuild` to reuse the last Desktop IDE build. A different file still goes through the recorder directly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\record-showcase.ps1 -Audio C:\path\to\track.mp3
```
