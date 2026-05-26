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

### 构建

```bash
dotnet build AC27Skin.csproj -c Release
```

编译器自动将 DLL 输出到 `BepInEx\plugins\` 和项目根目录两个位置。

### 项目结构

```
AC27Skin/
├── AC27Skin.csproj           # .NET 6, 目标 BepInEx IL2CPP
├── AC27SkinPlugin.cs          # BepInEx 插件入口：Load + SafePatchAll + 类型注册 + 场景注入
├── ModBehaviour.cs            # DontDestroyOnLoad MonoBehaviour：每帧巡检 + 分辨率监控 + Tip守护
├── TextSystem.cs              # TextReplacer + TextOverridesConfig + DelayedTextUpdater + LoadingTipOverride
├── PatchesMenu.cs             # MainMenuView/QuitView/QuitWithWishlistView/LiveryModdingView 补丁 + LogoReplacer
├── PatchesLevelSelect.cs      # LevelSelectView 全家桶：景区选择/关卡列表/Start按钮/背景图/机场预览
├── PatchesSettings.cs         # 设置面板文字替换
├── PatchesAircraft.cs         # 航班方向/呼号/滑行速度覆盖（全部默认禁用）
└── overrides/                 # 替换资源包
    ├── text.txt               # 文本替换规则（key:value 每行一条）
    ├── title.png              # 主菜单标题图
    ├── icon1.png ~ icon3.png  # 按钮图标
    └── kjfk/ zsjn/ tutorial/  # 机场背景 + 航图 + 按钮预览
```

### 核心架构总览

**三层职责分离：**

| 层 | 文件 | 职责 |
|---|---|---|
| **插件入口 & Lifecycle** | `AC27SkinPlugin.cs` | `Load()` 注册所有 Harmony patch + IL2CPP 类型注入 + `SceneManager.sceneLoaded` 回调创建 `AC27Skin_Manager` GameObject |
| **Harmony 补丁层** | `Patches*.cs` | 拦截游戏方法执行时机，在 Postfix/Prefix 中调用 TextReplacer / LogoReplacer 完成替换 |
| **替换引擎层** | `TextSystem.cs`, `PatchesMenu.cs`（LogoReplacer） | 纯静态工具方法：解析 overrides、遍历 GameObject 子树、安全写 TMP 文字、加载/缩放/替换 Sprite |

#### 插件入口：`AC27SkinPlugin.cs`

**`Load()` 流程：**

1. `SafePatchAll(harmony, patchType)` — 手动解析 `TargetMethod()` 或 `[HarmonyPatch]` 属性，逐个注册 Prefix/Postfix，**从不调用 `Assembly.GetTypes()`**（IL2CPP 下会抛 `TypeLoadException` 崩溃）。
2. 注册 20+ 个 Harmony patch（详见下方补丁一览表）。
3. `ClassInjector.RegisterTypeInIl2Cpp<ModBehaviour>()` — 将自定义 MonoBehaviour 注入 IL2CPP 类型系统，否则 `AddComponent<T>()` 会失败。
4. `SceneManager.add_sceneLoaded(OnSceneLoaded)` — 每次场景加载时创建 `DontDestroyOnLoad` 的 `AC27Skin_Manager` 并附加 `ModBehaviour`。

#### 文本替换引擎：`TextSystem.cs`

**配置加载 — `TextOverridesConfig`（懒加载）**

- 首次访问任意属性时读取 `overrides/text.txt`
- 解析规则：每行 `原始文本:替换文本`，`#` 注释，空行忽略
- 特殊键：`tips:` → 加载提示固定文字；`version:` → 版本后缀；`CCC Games:` → 公司名替换
- `AllTextSorted` — 按 **key 长度降序** 排序，确保 `"结束演示"` 先于 `"结束"` 匹配，避免短字符串误吞

**替换策略 — `TextReplacer`（5 大方法）**

| 方法 | 作用 |
|---|---|
| `DisableAllLocalization()` | **第一步，必须先做**：禁用 GameObject 子树中所有 `LocalizeStringEvent` 组件，阻止 Unity Localization 把文字刷回原文 |
| `ReplaceButtonTexts()` | 扫描所有 `Button` → 其下 TMP 文字 + 所有顶层 TMP（跳过 VersionText），按 `AllTextSorted` 替换 |
| `ReplaceVersionText()` | 专门处理版本号行：匹配 `playtest/0000` / `游戏版本:xxx`，追加 `伊雷娜版\n制作组` |
| `ReplaceCompanyText()` | 全文替换 `CCC Games` → 自定义制作组名 |
| `ReplaceTexts()` | 通用替换：遍历所有 TMP，按 text.txt 规则替换，替换后调用 `TryDisableLocalization()` 逐级锁死 |

**`SafeSetTMPText()`** — 安全设置文字：`tmp.text = newText` + `ForceMeshUpdate(true, true)` 确保即时渲染生效。

**`NormalizeForMatch()`** — 去掉所有换行符再做 `Contains`，避免游戏 TMP 内部的 `\n` 导致匹配失败。

**`DelayedTextUpdater`** — 附加到 View GameObject 的临时 MonoBehaviour：
- 在 `LateUpdate` 中逐帧重新执行替换（捕获异步加载的 UI 子元素）
- 连续 60 帧稳定无改动 或 300 帧上限 后自我销毁
- 首帧 dump 所有 MonoBehaviour 类型名，辅助逆向

**LoadingView 提示覆盖（三层保险）**

| 层 | 机制 | 触发时机 |
|---|---|---|
| Hook 1: `OnStringLoadedPrefix` | Harmony Prefix 拦截 localization 回调，直接改写 `newText` 参数 | 游戏本地化系统加载提示字符串时 |
| Hook 2: `ShowPostfix` | Harmony Postfix 在 `LoadingView.Show()` 后立即禁用 `_tipsTextEvent` + 写死 `_tipsText.text` | 每次进入关卡/切换场景 |
| Guard: `ModBehaviour.Update` | 每 0.3s 巡检一次，通过反射取 `_tipsText` 字段 + 写 `m_text` 内部字段（双保险 IL2CPP） | 持续运行 |

#### Logo 替换引擎：`PatchesMenu.cs`（`LogoReplacer`）

- 从 `overrides/title.png` 加载 `Texture2D` → 创建 `Sprite`（缓存，跨场景复用）
- 通过 `transform.Find("LogoHolder")` 找到目标节点（非反射，因为 IL2CPP 下 `GetField` 返回 null）
- 遍历 `LogoHolder` 及其所有子节点的 `Image` / `RawImage` 组件，替换 sprite/texture
- **动态缩放公式：** `dynamicScale = BaseLogoScale * (screenWidth/1920) / max(canvasScale, 0.3)`
  - 适配不同分辨率：4K 下比例翻倍，保证视觉占比一致
  - 适配 UI 缩放设置：50% UI 时 `canvasScale≈0.5`，localScale 翻倍补偿

#### IL2CPP 兼容性专项设计

本项目专门适配 IL2CPP（C# → C++ AOT 编译），以下坑点全部有专项方案：

| IL2CPP 陷阱 | 表现 | 本项目解决方案 |
|---|---|---|
| `Assembly.GetTypes()` 崩溃 | 触发 `TypeLoadException`（IL2CPP 类型无法通过 CLR 反射加载） | `SafePatchAll` — 用 `TargetMethod()` 静态方法 + `AccessTools.Method/TypeByName` 逐个解析 target，不回退到 `PatchAll` |
| `GetField("FieldName")` 返回 null | IL2CPP 剪裁掉非引用字段的 CLR 元数据 | 优先用 `transform.Find("path/to/GameObject")` 走 hierarchy 查找；必须用反射时保留 fallback |
| `Texture2D/Sprite` 在场景卸载后被销毁但 managed wrapper 非 null | `!= null` 对已销毁 IL2CPP 对象仍返回 true | `LogoReplacer.EnsureLoaded()` 在每次调用时校验缓存有效性，场景卸载后自动重载 |
| 自定义 `MonoBehaviour` 无法通过 `AddComponent<T>()` 附加 | IL2CPP 只认可预先注册的类型 | `ClassInjector.RegisterTypeInIl2Cpp<T>()` 在 `Load()` 中统一注册：`ModBehaviour`, `DelayedTextUpdater`, `BackgroundGuard`, `SettingsDelayedUpdater`, `StartBtnWatcher` |

#### Harmony 补丁一览

**主菜单 (PatchesMenu.cs)**

| 补丁类 | 拦截方法 | 类型 | 触发时机 |
|---|---|---|---|
| `MainMenuPatch` | `MainMenuView.OnEnable()` | Postfix | 主菜单显示 → 禁本地化 → 换文字 → 换Logo → 调度 DelayedTextUpdater |
| `UpdateLogoPatch` | `MainMenuView.UpdateLogo()` | Postfix | Logo 初始化/刷新 → 重新替换（覆盖游戏的异步加载） |
| `QuitViewPatch` | `QuitView.OnEnable()` | Postfix | 退出确认弹窗 → 替换"退出游戏"等文字 |
| `QuitWithWishlistViewPatch` | `QuitWithWishlistView.OnEnable()` | Postfix | 退出+wishlist 弹窗 |
| `LiveryModdingViewProxyPatch` | `LiveryModdingViewProxy.Awake()` | Postfix | 模组/涂装面板 → 替换所有文字 |

**设置面板 (PatchesSettings.cs)**

| 补丁类 | 拦截方法 | 效果 |
|---|---|---|
| `SettingsTextPatch` | `SettingsView.OnEnable()` | 替换设置页所有 TMP 文字 + 版本号 |
| `SettingsTextPatch.ShowPatch` | `SettingsView.Show()` | 覆盖首次显示时的文字 |

**选关界面 (PatchesLevelSelect.cs)**

| 补丁类 | 拦截方法 | 效果 |
|---|---|---|
| `LvSel_Init` | `LevelSelectView.Init()` | 替换关卡列表文字 |
| `LvSel_Review` | `DisplayAirportReview()` | 替换机场预览描述、图例 |
| `LvSel_HideReview` | `HideAirportReview()` | 隐藏时清理状态 |
| `LvSel_ShowLevel` | `ShowLevel()` | 替换关卡卡片文字 |
| `LvSel_ShowAirport` | `ShowAirport()` | 替换机场名称 |
| `LvSel_AirportList` | `AirportList()` | 替换机场列表项文字 |
| `LvSel_LevelPartName` | `LevelPartName()` | 替换关卡区块名称 |
| `LvSel_LevelList` | `LevelList()` | 替换关卡列表 |
| `LvSel_UpdateStartBtn` | `UpdateStartBtn(bool)` | 点击关卡的**同一帧**修复 Start 按钮文字（`"开始关卡"→"翻开此章"`） |
| `LvSel_InitBg` | `InitializeBackgroundImages()` | 替换景区背景图 + 航图 + 按钮预览 |
| `AirportItem_Exit` | `AirportItem.OnExitButton()` | 退出机场详情时清理 |
| `AirportItem_Create` | `AirportItem.Create()` | 创建机场项时替换预览图 |
| `AirportItem_Enter` | `AirportItem.OnEnterButton()` | 进入机场时触发 |

**航班系统 (PatchesAircraft.cs) — 全部默认禁用**

保留骨架代码，取消注释即可启用：`FlightDirectionOverride`（强制 Arrival/Departure）、`CallSignOverride`（替换呼号）、`TaxiSpeedOverride`（加速滑行）、`DynamicsUpdatePostfix`（滑行速度后处理）。

#### 运行时管理：`ModBehaviour.cs`

`ModBehaviour` 挂载在 `DontDestroyOnLoad` 的 `AC27Skin_Manager` 上，跨场景持续运行：

| 系统 | 频率 | 功能 |
|---|---|---|
| **Tip 守护** | 每 0.3s | 缓存 `LoadingView._tipsText`、禁用 `_tipsTextEvent`、写死固定提示文字、同时写 `m_text` 内部字段做双保险 |
| **分辨率监控** | 每帧检测 `Screen.width/height` | 分辨率/DPI 变更时重新扫描所有 `MainMenuView`，重做 Logo+文字替换（`CanvasScaler` 会改变 scale，Logo 需要重算 `dynamicScale`） |
| **StartBtn 巡检** | 每 10 帧扫描一次场景，缓存 `LevelSelectView`，逐帧检测 `LevelPart/Start` 的文本 | fallback 机制：如果 `LvSel_UpdateStartBtn` Harmony 补丁因任何原因未生效，此每帧巡检会兜底修复 Start 按钮文字 |
| **Logo 缓存清理** | 场景卸载时自动 | `LogoReplacer.EnsureLoaded()` 检测到缓存 Texture 已被 Native 端销毁，清空后重新加载 |

#### 关卡选择 Start 按钮修复详解

这是整个项目中最微妙的 IL2CPP 适配案例：

- **挑战：** `LevelSelectView` 有一个 private 字段 `StartBtn`（Unity `Button`），游戏在选中关卡后调用 `UpdateStartBtn(true)` 激活按钮并设置文字
- **IL2CPP 问题：** `GetField("StartBtn")` 始终返回 `null`（AOT 编译器可能内联或瘦身了字段元数据）
- **主方案：** Harmony patch `LvSel_UpdateStartBtn`，Postfix 同步于 `UpdateStartBtn(bool)`，通过 `transform.Find("LevelPart/Start")` 直接走 hierarchy 找到按钮，在同一帧替换 TMP 文字
- **兜底方案：** `ModBehaviour.TryFixStartBtnText()` 每帧巡检，同样用 `transform.Find` 而不是反射

#### 添加新文字替换

1. 编辑 `overrides/text.txt`，添加 `原始文本:替换文本` 行
2. 重启游戏即生效（`TextOverridesConfig` 每次启动重新加载）

#### 添加新的 Harmony 补丁

1. 在对应 `Patches*.cs` 中添加 `[HarmonyPatch]` 静态类
2. 实现 `static MethodBase TargetMethod()` 返回目标方法（用 `AccessTools.Method/TypeByName`）
3. 在 `Prefix/Postfix` 中调用 `TextReplacer` / `LogoReplacer` 方法
4. 在 `AC27SkinPlugin.Load()` 中添加 `TryPatch("PatchName", typeof(YourPatch))`
5. `dotnet build -c Release`

#### 注意事项

- **IL2CPP 反射限制：** C# `GetField()` / `GetProperty()` 在 IL2CPP 下可能静默返回 null。优先使用 `transform.Find()` 走 GameObject 层级，或 `GetComponent<T>()` 走 Component 查找。
- **本地化系统：** Unity Localization 包会在每帧回调中覆盖 `TMP_Text.text`。所有文字替换前必须先 `DisableAllLocalization()`。
- **Async UI：** 部分 UI 是异步生成的（如 `UpdateLogo` 动态创建子对象），需要用 `DelayedTextUpdater` 在后续几帧重复应用替换。


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

### Build

```bash
dotnet build AC27Skin.csproj -c Release
```

The DLL is automatically output to both `BepInEx\plugins\` and the project root.

### Project Structure

```
AC27Skin/
├── AC27Skin.csproj          # .NET 6, targeting BepInEx IL2CPP
├── AC27SkinPlugin.cs         # BepInEx plugin entry: Load, SafePatchAll, type registration, scene injection
├── ModBehaviour.cs           # DontDestroyOnLoad MonoBehaviour: per-frame scanning, resolution monitor, tip guard
├── TextSystem.cs             # TextReplacer + TextOverridesConfig + DelayedTextUpdater + LoadingTipOverride
├── PatchesMenu.cs            # MainMenu/Quit/LiveryModding patches + LogoReplacer
├── PatchesLevelSelect.cs     # LevelSelectView: airport selection, level list, Start button, backgrounds
├── PatchesSettings.cs        # Settings panel text replacement
├── PatchesAircraft.cs        # Flight direction/callsign/taxi speed overrides (all disabled by default)
└── overrides/                # Replacement assets
    ├── text.txt              # Text replacement rules (key:value, one per line)
    ├── title.png             # Main menu title image
    └── kjfk/ zsjn/ tutorial/ # Airport backgrounds, diagrams, button previews
```

### Architecture Overview

**Three-layer separation:**

| Layer | Files | Responsibility |
|---|---|---|
| **Plugin Entry** | `AC27SkinPlugin.cs` | `Load()` registers all Harmony patches + IL2CPP type injection + `SceneManager.sceneLoaded` callback |
| **Harmony Patches** | `Patches*.cs` | Intercept game method timings; call TextReplacer / LogoReplacer in Postfix/Prefix |
| **Replacement Engine** | `TextSystem.cs`, LogoReplacer in `PatchesMenu.cs` | Pure static utilities: parse overrides, walk GameObject trees, safely write TMP text, load/replace Sprites |

#### Plugin Entry: `AC27SkinPlugin.cs`

**`Load()` flow:**

1. `SafePatchAll(harmony, patchType)` — manually resolves `TargetMethod()` or `[HarmonyPatch]` attributes, registers Prefix/Postfix individually. **Never calls `Assembly.GetTypes()`** (crashes under IL2CPP with `TypeLoadException`).
2. Registers 20+ Harmony patches via `TryPatch()`.
3. `ClassInjector.RegisterTypeInIl2Cpp<T>()` — injects custom MonoBehaviours into the IL2CPP type system so `AddComponent<T>()` works.
4. `SceneManager.add_sceneLoaded(OnSceneLoaded)` — creates the `DontDestroyOnLoad` `AC27Skin_Manager` GameObject with `ModBehaviour` attached.

#### Text Replacement Engine: `TextSystem.cs`

**Config loading — `TextOverridesConfig` (lazy)**

- Loads `overrides/text.txt` on first access
- Format: `original text:replacement text`, `#` for comments, blank lines ignored
- Special keys: `tips:` → fixed loading tip; `version:` → version suffix; `CCC Games:` → company name
- `AllTextSorted` — sorted by **key length descending** so `"结束演示"` matches before `"结束"`

**Replacement strategy — `TextReplacer` (5 methods)**

| Method | Purpose |
|---|---|
| `DisableAllLocalization()` | First step: disable all `LocalizeStringEvent` components to block localization overwrites |
| `ReplaceButtonTexts()` | Scan all Buttons → their TMP text + all top-level TMP, replace using `AllTextSorted` |
| `ReplaceVersionText()` | Match version line pattern, append custom suffix + company name |
| `ReplaceCompanyText()` | Replace `CCC Games` with custom name across all TMP |
| `ReplaceTexts()` | Generic replacement: iterate all TMP, apply text.txt rules, then `TryDisableLocalization()` |

**`SafeSetTMPText()`** — `tmp.text = newText` + `ForceMeshUpdate(true, true)` for instant rendering.

**`NormalizeForMatch()`** — strips newlines before `Contains()` to handle game TMP embedded `\n`.

**`DelayedTextUpdater`** — temporary MonoBehaviour attached to View GameObjects:
- Runs in `LateUpdate`, re-applying replacements each frame (catches async-loaded UI children)
- Self-destructs after 60 stable frames or 300 total frames
- First frame dumps all MonoBehaviour type names for reverse-engineering

**LoadingView Tip Override (3-layer safety net)**

| Layer | Mechanism | Trigger |
|---|---|---|
| Hook 1: `OnStringLoadedPrefix` | Harmony Prefix intercepts localization callback, rewrites `newText` parameter | When localization system loads a tip string |
| Hook 2: `ShowPostfix` | Harmony Postfix after `LoadingView.Show()`: disables `_tipsTextEvent` + writes `_tipsText.text` | Every level/scene load |
| Guard: `ModBehaviour.Update` | Polls every 0.3s via reflection + `m_text` internal field (belt-and-suspenders for IL2CPP) | Continuous |

#### Logo Replacement Engine: `LogoReplacer` (in `PatchesMenu.cs`)

- Loads `overrides/title.png` → creates cached `Texture2D` + `Sprite`
- Finds target via `transform.Find("LogoHolder")` (not reflection — `GetField` returns null under IL2CPP)
- Replaces all `Image` / `RawImage` components on LogoHolder and its children
- **Dynamic scaling formula:** `dynamicScale = BaseLogoScale * (screenWidth/1920) / max(canvasScale, 0.3)`
  - Compensates for different resolutions (4K = 2× scale)
  - Compensates for UI scale settings (50% UI → 2× localScale)

#### IL2CPP Compatibility

This project is specifically engineered for IL2CPP (C# → C++ AOT compilation). Every known pitfall has a workaround:

| IL2CPP Trap | Symptom | Our Solution |
|---|---|---|
| `Assembly.GetTypes()` crash | `TypeLoadException` | `SafePatchAll` — uses `TargetMethod()` + `AccessTools.Method/TypeByName`, never `PatchAll` |
| `GetField("name")` returns null | IL2CPP strips CLR metadata for non-referenced fields | Prefer `transform.Find("path")` for hierarchy lookups; keep fallbacks when reflection is required |
| Destroyed Native objects appear non-null | `!= null` still returns true for destroyed IL2CPP objects | `LogoReplacer.EnsureLoaded()` validates cache on each call, auto-reloads after scene unload |
| `AddComponent<T>()` fails for custom types | IL2CPP only recognizes pre-registered types | `ClassInjector.RegisterTypeInIl2Cpp<T>()` in `Load()`: registers `ModBehaviour`, `DelayedTextUpdater`, etc. |

#### Harmony Patch Reference

**Main Menu (PatchesMenu.cs)**

| Patch Class | Intercepted Method | Type | When |
|---|---|---|---|
| `MainMenuPatch` | `MainMenuView.OnEnable()` | Postfix | Main menu display → disable localization → replace text → replace logo → schedule DelayedTextUpdater |
| `UpdateLogoPatch` | `MainMenuView.UpdateLogo()` | Postfix | Logo init/refresh → re-apply (overwrites async-loaded children) |
| `QuitViewPatch` | `QuitView.OnEnable()` | Postfix | Exit confirmation dialog |
| `QuitWithWishlistViewPatch` | `QuitWithWishlistView.OnEnable()` | Postfix | Exit+wishlist dialog |
| `LiveryModdingViewProxyPatch` | `LiveryModdingViewProxy.Awake()` | Postfix | Mod/livery panel |

**Level Select (PatchesLevelSelect.cs)**

| Patch Class | Intercepted Method | Effect |
|---|---|---|
| `LvSel_Init` | `LevelSelectView.Init()` | Replace level list text |
| `LvSel_Review` | `DisplayAirportReview()` | Replace airport description, diagram |
| `LvSel_ShowLevel` | `ShowLevel()` | Replace level card text |
| `LvSel_ShowAirport` | `ShowAirport()` | Replace airport name |
| `LvSel_UpdateStartBtn` | `UpdateStartBtn(bool)` | Fix Start button text on the **same frame** a level is selected |
| `LvSel_InitBg` | `InitializeBackgroundImages()` | Replace airport backgrounds, diagrams, button previews |
| `AirportItem_Create` | `AirportItem.Create()` | Replace preview images on create |

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
