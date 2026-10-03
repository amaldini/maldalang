# Showcase song prompt

Paste-ready prompt for a generated soundtrack to the Desktop IDE reel.
The reel order is `scripts/showcase/playlist.json`. Record with
`scripts/record-showcase.ps1` and pass the finished track as `-Audio`.
No music is bundled in the repository. Use a track you have rights to use.
The video is trimmed to whichever of the picture and the track is shorter.

Tests of generated tracks settled on a natural female voice, a dreaming
atmosphere, and soft melodic music. Vocoder and robotic delivery were
rejected.

Each cue has `startMs`, milliseconds from the splash. That cue stays on
screen until the next cue's `startMs`. `endMs` is the end of the reel.
The recorder trims its lead-in so the video file starts on that same
clock, then muxes `-Audio` from the start of the file. Put a lyric on a
section by setting that cue's `startMs` to the lyric's time in the song.
`malda scripts/showcase/timeline_editor.malda` opens sliders for those
times at http://localhost:8094/. Earlier and Later reorder the example
browser, the manual pages, and the scenes inside their own tour. Each
scene has its dream lyric. Hear plays from that cue for the number of
seconds set on the page, starting at 3. Save writes the playlist and this prompt.
A scene still compiles at the beginning of its slot; the slot ends on
the next `startMs`, and a later cue waits for its own `startMs` when the
reel is ahead of the clock.

If the generator has a style box and a lyrics box, split on the headings
below. If it has one box, paste the style first. Bracketed lines are
directions and should not be sung.

## Style

```
80 BPM, soft dream-pop, airy and melodic. Natural female voice, breathy, close, gentle vibrato, long legato phrases, soft doubles in the distance. Dreaming atmosphere: wide pads, soft piano, felt electric piano, quiet synth pulse, light reverb and delay. Intimate, floating, hopeful, low volume, even dynamics. No vocoder, no robotic voice, no spoken checklist, no rap, no belting, no drums beyond a soft pulse, no drop, no distortion. About 1 minute 45 seconds. End by letting the last note fade into a resolved pad.
```

## Lyrics

```
[Intro – 5 seconds, pads only, then one soft line]
[Female voice, almost a whisper]
Malda, come closer.

[Catalog – 19 seconds, one continuous melody, four legato lines, soft and unhurried, do not accent every word]
[Female voice]
Hello, confirm. The schema settles. Objects come alive.
The tower moves. The network answers. A page is built.
The set opens. Light returns. The ground is solid.
Bricks will fall. The pole stays up. This lap is ours. The king is watched.

[Manual – 13 seconds, a little wider, still gentle, the middle line is the refrain]
[Female voice]
Open the book. The types lock in. Call the function.
Many agents, one objective.
Play it in the browser.

[Dream – 70 seconds, same tempo, hold each line, leave a soft instrumental breath after every line]
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

[Outro – pads, one fading line]
[Female voice, distant]
Malda.
```

## What each line follows

Catalog, in browse order: Hello, Schemas, Objects, Hanoi, XOR net,
UI controls, Mandelbrot, Raytracer, Platform, Maldanoid, CartPole,
Grand Prix, Chess.

Manual: the manual home page, data types, functions, neural nets
(“Many agents, one objective” is the multi-agent page), browser games.

Dream, in scene order: Hello, Schemas, Towers of Hanoi, UI controls,
Mandelbrot, GPU raytracer, GPU billiards, Platform, Maldanoid,
CartPole, Grand Prix, Chess.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\record-showcase.ps1 -Audio C:\path\to\track.mp3
```
