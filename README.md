# **Iyan-Kim UV Mask Tool**

### _Material-slot based UV island mask generation for Unity Editor_

`Iyan-Kim UV Mask Tool` is a Unity **Editor-only utility** for selecting UV islands from a renderer's material slot and exporting them as **PNG mask textures**.  
It helps artists and avatar creators quickly generate texture masks from complex meshes without manually repainting UV areas in external tools.

✔ 100% Editor-only tool  
✔ No runtime footprint  
✔ Material slot based UV island selection  
✔ PNG mask export for texture workflows  
✔ Designed for VPM / VCC distribution

---

## 🔧 **Features**

Iyan-Kim UV Mask Tool streamlines UV mask creation by providing:

### 🧩 **Material Slot Based UV Detection**

- Select a renderer and choose a **material slot**
- Automatically reads the triangles used by the selected material slot
- Detects UV islands from the selected slot only
- Works well for avatars, outfits, props, and modular meshes

### 🖱 **Interactive UV Island Selection**

- Click UV islands directly in the preview
- Select all, invert selection, or clear selection
- Hover and selection feedback in the UV preview
- Optional Scene View highlight for selected islands

### 🖼 **PNG Mask Export**

- Export selected UV islands as a PNG mask texture
- Resolution presets: `256`, `512`, `1024`, `2048`, `4096`
- Custom resolution support up to `8192`
- Configurable background color, selected color, and alpha
- Optional padding and anti-aliasing

### 🧭 **Renderer Coverage**

- Supports both `MeshRenderer` and `SkinnedMeshRenderer`
- Reads meshes from `MeshFilter.sharedMesh` or `SkinnedMeshRenderer.sharedMesh`
- Handles material slots with valid submesh triangles
- Automatically detects usable UV channels

### 🌐 **Multi-language UI**

- English / Korean / Japanese support
- Language can be switched directly in the window
- Language preference is saved in Unity Editor preferences

### 🛠 **Editor-friendly UX**

- Simple renderer object field workflow
- Zoomable and pannable UV preview
- Large export warning for high memory operations
- Clear status messages for missing meshes, UVs, and material slots

---

## 🚀 Installation (VPM / VCC)

### **Add repository to VCC**

Click:

👉 **[Add Iyan-Kim VPM Repository to VCC](vcc://vpm/addRepo?url=https://raw.githubusercontent.com/Yunhyuk-Jeong/iyan-vpm/main/vpm.json)**

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

## 🧠 How It Works

- Select a `MeshRenderer` or `SkinnedMeshRenderer`
- Choose the target **Material Slot**
- The tool reads the submesh triangles used by that slot
- Usable UV channels are detected automatically
- UV islands are generated from connected UV triangles
- Selected islands are rasterized into a PNG mask

💡 **Tip**  
For best results, use meshes with clean UV islands and enable **Read/Write** in the model import settings if Unity cannot read mesh data.

---

## 📁 Repository Structure

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
  LICENSE
  package.json
README.md
LICENSE
```

---

## 🛠 Menu Path

```text
Iyan-Kim/Tools/UV Island Mask Generator
```

---

## 📜 License

This project is released under the **MIT License**.

You are free to use, modify, and redistribute this tool in both personal and commercial projects.

---

## 🙏 Credits

**Tool Design & Implementation**: _Iyan-Kim_  
**Purpose**: Make UV island based mask generation faster for Unity, VRChat avatar, and texture authoring workflows.

---

## ❤️ Support / Feedback

If you encounter bugs or have feature requests,  
please open an issue or pull request on this repository.

Contributions are welcome!
