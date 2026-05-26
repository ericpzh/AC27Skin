# AC27 Skin

Airport Control 27 Playtest / 机场管制 27 Demo 的文本与贴图覆盖 Mod。

> [English](#english)

## 安装

### 第一步：安装 BepInEx（IL2CPP 版本）

> ⚠️ 此游戏使用 **IL2CPP** 运行时，需要 Bleeding Edge 版 BepInEx。

1. 打开 [BepInEx Bleeding Edge 构建页](https://builds.bepinex.dev/projects/bepinex_be)
2. 下载 **`BepInEx-Unity.IL2CPP-win-x64-*.zip`**（Windows x64 + IL2CPP`）
3. 解压这个 zip，你会得到：
   - `BepInEx/` 文件夹（含 IL2CPP 支持）
   - `dotnet/` 文件夹（.NET 运行时）
   - `doorstop_config.ini`
   - `winhttp.dll`

### 第二步：找到游戏安装目录

1. 打开 **Steam** → 右键 **Airport Control 27 Playtest** 或者 **机场管制 27 Demo** → **管理** → **浏览本地文件**
2. 弹出来的文件夹就是游戏根目录（例如 `...\steamapps\common\Airport Control 27 Playtest`）
3. **将解压出的全部 4 项复制到游戏根目录**：
   - `BepInEx/` 文件夹
   - `dotnet/` 文件夹
   - `doorstop_config.ini`
   - `winhttp.dll`

### 第三步：首次运行游戏（初始化 BepInEx）

1. 启动一次游戏，会弹出一个 BepInEx 控制台窗口
2. 等游戏进入主菜单后**退出游戏**
3. 此时 BepInEx 已初始化完成，`BepInEx/plugins/` 等目录已自动生成

### 第四步：安装 AC27 Skin

从 [Releases](https://github.com/ericpzh/AC27Skin/releases/latest) 下载 `AC27Skin.zip`，解压得到：
- `AC27Skin.dll` → 插件本体
- `overrides/` → 贴图和文本替换资源

1. 把 `AC27Skin.dll` 复制到 `BepInEx\plugins\` 目录下
2. 把 `overrides/` 文件夹复制到游戏根目录
3. 最终结构如下：

```
Airport Control 27 Playtest/
├── doorstop_config.ini
├── winhttp.dll
├── BepInEx/
│   ├── core/
│   └── plugins/
│       └── AC27Skin.dll       ← 放这里
├── overrides/                  ← 放这里
│   ├── text.txt                ← 文本替换规则
│   ├── title.png               ← 主菜单标题图
│   ├── icon1.png ~ icon3.png   ← 按钮图标
│   └── kjfk/ ... zsjn/ ...     ← 机场贴图
└── ...
```

### 第五步：重新启动游戏

再次启动游戏，BepInEx 控制台弹出时看到 `[AC27Skin]` 日志即为安装成功。

---

## 自定义文本

编辑 `overrides/text.txt`，格式为每行一条替换规则：

```
原始文本:替换后的文本
```

示例：

```
退出游戏:合上日记
确认退出游戏吗:今天的故事就记录到这里吗
```

重启游戏后生效

## 自定义图片

替换 `overrides/` 下对应的 PNG 文件即可：

| 文件 | 用途 |
|------|------|
| `title.png` | 主菜单标题 |
| `icon1.png` ~ `icon3.png` | 主菜单按钮图标 |
| `kjfk/`, `zsjn/`, `tutorial/` | 机场选择卡片背景 |


---

## 开发

```bash
dotnet build AC27Skin.csproj -c Release
```

编译器会自动把 DLL 输出到 `BepInEx\plugins\` 和项目根目录。


# English

Text and texture override mod for Airport Control 27 Playtest (机场管制 27 Demo).

## Installation

### Step 1: Install BepInEx (IL2CPP version)

> ⚠️ This game uses the **IL2CPP** runtime. You need the Bleeding Edge build.  

1. Go to [BepInEx Bleeding Edge builds](https://builds.bepinex.dev/projects/bepinex_be)
2. Download **`BepInEx-Unity.IL2CPP-win-x64-*.zip`** (Windows x64 + IL2CPP)
3. Extract the zip. You should see:
   - `BepInEx/` folder (with IL2CPP support)
   - `dotnet/` folder (.NET runtime)
   - `doorstop_config.ini`
   - `winhttp.dll`


### Step 2: Locate the game folder

1. Open **Steam** → right-click **Airport Control 27 Playtest** (or **Airport Control 27 Demo**) → **Manage** → **Browse local files**
2. The folder that opens is the game root (e.g. `...\steamapps\common\Airport Control 27 Playtest`)
3. **Copy all 4 extracted items into the game root**:
   - `BepInEx/` folder
   - `dotnet/` folder
   - `doorstop_config.ini`
   - `winhttp.dll`

### Step 3: Launch the game once (initialize BepInEx)

1. Start the game — a BepInEx console window will appear
2. Wait until the main menu loads, then **exit the game**
3. BepInEx is now initialized and `BepInEx/plugins/` has been created

### Step 4: Install AC27 Skin

Download `AC27Skin.zip` from [Releases](https://github.com/ericpzh/AC27Skin/releases/latest) and extract:
- `AC27Skin.dll` → the plugin
- `overrides/` → texture & text replacement assets

1. Copy `AC27Skin.dll` into `BepInEx\plugins\`
2. Copy the `overrides/` folder into the game root
3. Final folder structure:

```
Airport Control 27 Playtest/
├── doorstop_config.ini
├── winhttp.dll
├── BepInEx/
│   ├── core/
│   └── plugins/
│       └── AC27Skin.dll       ← put here
├── overrides/                  ← put here
│   ├── text.txt                ← text replacement rules
│   ├── title.png               ← main menu title image
│   ├── icon1.png ~ icon3.png   ← button icons
│   └── kjfk/ ... zsjn/ ...     ← airport textures
└── ...
```

### Step 5: Restart the game

Launch the game again. When the BepInEx console shows `[AC27Skin]` log entries, the mod is working.

---

## Customizing Text

Edit `overrides/text.txt`. Each line is one replacement rule:

```
original text:replacement text
```

Example:

```
退出游戏:Quit Game
确认退出游戏吗:Are you sure you want to quit?
```

- Lines starting with `#` are comments
- Empty lines are ignored
- Restart the game for changes to take effect

## Customizing Images

Replace the corresponding PNG files under `overrides/`:

| File | Purpose |
|------|------|
| `title.png` | Main menu title |
| `icon1.png` ~ `icon3.png` | Main menu button icons |
| `kjfk/`, `zsjn/`, `tutorial/` | Airport selection card backgrounds |

## Uninstall

Delete `BepInEx\plugins\AC27Skin.dll` and the `overrides/` folder.

---

## Development

```bash
dotnet build AC27Skin.csproj -c Release
```

The DLL will be automatically output to both `BepInEx\plugins\` and the project root.

### Creating a Release

```bash
# 1. Build, commit & tag
dotnet build -c Release
git add AC27Skin.dll
git commit -m "release: v1.0.0"
git tag v1.0.0
git push origin main --tags

# 2. GitHub Actions auto-creates the Release with AC27Skin.zip

# Or locally:
.\package.ps1
```
