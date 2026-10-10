# Galaxy Bridge application icon

galaxybridge.png is the original user-supplied 1536 × 1536 image.
Windows uses a multi-resolution ICO embedded in the EXE and managed assembly.
Both Android companions use density-specific legacy PNGs and an adaptive icon.
The adaptive foreground fits the original image into 70 dp of the 108 dp layer
to keep the monitor and phone inside launcher masks.

All generated images are committed, so the build needs no image-processing
dependency. Android Edge now compiles its resources before linking its manifest.
