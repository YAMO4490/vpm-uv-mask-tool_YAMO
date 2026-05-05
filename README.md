# **Studio Iyan UV Mask Tool**

### _Material-slot based UV island and face mask generation for Unity Editor_

`Studio Iyan UV Mask Tool` is a Unity **Editor-only utility** for selecting UV islands or individual UV faces from a renderer's material slot and exporting them as **PNG mask textures**.  
It helps artists and avatar creators generate texture masks from complex meshes without manually repainting UV areas in external tools.

- 100% Editor-only tool
- No runtime footprint
- Material slot based UV selection
- Island / Face selection workflow
- PNG mask export for texture workflows
- Designed for VPM / VCC distribution

---

## Features

Studio Iyan UV Mask Tool streamlines UV mask creation by providing:

### Material Slot Based UV Detection

- Select a renderer and choose a **material slot**
- Automatically reads the triangles used by the selected material slot
- Detects UV islands from the selected slot only
- Works well for avatars, outfits, props, and modular meshes

### Interactive UV Selection

- Select by **UV island** or by **individual face**
- Click directly in the UV preview
- Brush workflow for face selection
  - `LMB` drag to paint
  - `RMB` drag to erase
- Select all, invert selection, or clear selection
- Hover and selection feedback in the UV preview
- Optional Scene View highlight for selected regions

### Preview Workflow

- Optional preview texture overlay in the UV preview
- Zoomable and pannable UV preview
- Configurable `UV Wireframe Color`
- Separate `Preview Color` and export `Selected Color`
- Optional toggle to pause Scene overlay updates while brushing

### PNG Mask Export

- Export selected UV regions as a PNG mask texture
- Resolution presets: `256`, `512`, `1024`, `2048`, `4096`
- Custom resolution support up to `8192`
- Configurable background color, selected color, and alpha
- Optional padding and anti-aliasing

### Renderer Coverage

- Supports both `MeshRenderer` and `SkinnedMeshRenderer`
- Reads meshes from `MeshFilter.sharedMesh` or `SkinnedMeshRenderer.sharedMesh`
- Handles material slots with valid submesh triangles
- Automatically detects usable UV channels

### Multi-language UI

- English / Korean / Japanese support
- Language can be switched directly in the window
- Language preference is saved in Unity Editor preferences

### Editor-friendly UX

- Simple renderer object field workflow
- Clear status messages for missing meshes, UVs, and material slots
- Large export warning for high memory operations
- Safe Editor-only workflow for iterative texture authoring

---

## Installation (VPM / VCC)

### **Add repository to VCC**

Click:

**[Add Studio Iyan VPM Repository to VCC](vcc://vpm/addRepo?url=https://raw.githubusercontent.com/Yunhyuk-Jeong/iyan-vpm/main/vpm.json)**

Or add manually:

```text
https://raw.githubusercontent.com/Yunhyuk-Jeong/iyan-vpm/main/vpm.json
```

Then install:

### **Package ID**

```text
com.iyankim.uvmasktool
```

---

## How It Works

- Select a `MeshRenderer` or `SkinnedMeshRenderer`
- Choose the target **Material Slot**
- The tool reads the submesh triangles used by that slot
- Usable UV channels are detected automatically
- UV islands are generated from connected UV triangles
- You can select by island, by face, or by brush in face mode
- Selected regions are rasterized into a PNG mask

**Tip**  
For best results, use meshes with clean UV islands and enable **Read/Write** in the model import settings if Unity cannot read mesh data.

---

## Update Log

### **1.3.0 - 2026-05-05**

#### **Added**

- Face selection mode for selecting individual UV triangles
- Brush workflow for face selection
  - `LMB` drag to paint
  - `RMB` drag to erase
- Preview texture overlay support in the UV preview
- Configurable `UV Wireframe Color`
- Separate `Preview Color` and export `Selected Color`
- Optional toggle to pause Scene overlay updates while brushing

#### **Improved**

- Scene View overlay alignment for current pose / transform on `SkinnedMeshRenderer`
- Scene overlay rendering for the current submesh selection
- UV preview interaction with zoom, pan, texture overlay, and face-level feedback
- Brush candidate lookup using cached UV-space bins
- Hover picking narrowed to cached candidate triangles
- Reduced Scene View repaints during brush interaction

#### **Changed**

- Scene View preview now uses the current posed geometry for better selection matching
- Face selection no longer requires one-by-one clicking only; drag painting is supported
- Default `UV Wireframe Color` is now black

---

## Repository Structure

```text
com.iyankim.uvmasktool/
  Editor/
    UVMaskWindow.cs
    UVIslandDetector.cs
    UVSelectionController.cs
    UVPreviewRenderer.cs
    UVRasterizer.cs
    UVPaddingProcessor.cs
    UVExporter.cs
    UVMaskSceneOverlay.shader
  package.json
README.md
LICENSE
```

---

## Menu Path

```text
Studio Iyan/Tools/UV Island Mask Generator
```

---

## License

This project is released under the **MIT License**.

You are free to use, modify, and redistribute this tool in both personal and commercial projects.

---

## Credits

**Tool Design & Implementation**: _Studio Iyan_  
**Purpose**: Make UV-based mask generation faster for Unity, VRChat avatar, and texture authoring workflows.

---

## Support / Feedback

If you encounter bugs or have feature requests,  
please open an issue or pull request on this repository.

Contributions are welcome!
