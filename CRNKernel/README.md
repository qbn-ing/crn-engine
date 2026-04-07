# CRNKernel - Jupyter Kernel for Chemical Reaction Networks

CRNKernel 是一个 Jupyter Notebook 内核，用于编程和分析生化系统。它支持以下领域特定语言 (DSL)：

- **CRN** (Chemical Reaction Networks) - 化学反应网络
- **DSD** (Visual DSD / ClassicDSD) - DNA 链置换电路编程

## 功能特性

- ✅ 支持 CRN/DSD 语言编程（使用原项目 ClassicDSD/CRN 解析器）
- ✅ 单元格之间自动传递物质浓度状态
- ✅ 支持多种模拟器（随机、确定性、JIT 等）
- ✅ 实时显示物质浓度变化图表（Plotly 交互式）
- ✅ 支持指令控制模拟参数
- ✅ 完整的 Jupyter 消息协议支持

## 安装

### 前提条件

1. **.NET Core SDK 3.1** 或更高版本
2. **Jupyter Notebook** 5.0 或更高版本

### 步骤

#### 1. 构建项目

```bash
cd CRNKernel
dotnet restore
dotnet build
```

#### 2. 安装 Jupyter 内核

**Windows - 安装到系统 Jupyter:**
```bash
install-kernel.bat
```

**Windows - 安装到 Conda 环境:**
```bash
install-kernel.bat --conda crn-test
```
将 `crn-test` 替换为你的 conda 环境名称。

**Linux/macOS:**
```bash
chmod +x install-kernel.sh
./install-kernel.sh
```

#### 3. 验证安装

```bash
jupyter kernelspec list
```

应该能看到 `crn` 内核。

## 使用方法

### 启动 Jupyter Notebook

```bash
jupyter notebook
```

在新建笔记本时选择 "CRN (F#)" 内核。新建代码时选用“纯文本”

### 可用指令

| 指令 | 描述 | 示例 |
|------|------|------|
| `%crn` | 使用 CRN 语法 | `%crn` |
| `%dsd` | 使用 DSD 语法 | `%dsd` |
| `%reset` | 重置物质状态（保留宏定义和其他设置） | `%reset` |
| `%reset all` | 重置所有状态（包括宏定义、调试设置等） | `%reset all` |
| `%help` | 显示帮助信息 | `%help` |
| `%csv` | 启用 CSV 导出（当前单元格） | `%csv` |
| `%title "name"` | 设置 CSV 导出标题 | `%title "实验 1"` |
| `%debug on` / `%debug off` | 启用/禁用调试输出（默认 off） | `%debug on` |
| `%export expanded` | 显示宏展开后的完整代码 | `%export expanded` |
| `%macro reset` | 重置宏注册表（清除所有宏定义） | `%macro reset` |
| `%macro list` | 列出所有已定义的宏 | `%macro list` |
| `%preserve XP XN` / `%保留 XP XN` | 指定保留的物质（仅当前单元格有效） | `%preserve XP` |

**注意**：
- 模拟参数（时间、点数、模拟器类型等）请使用 CRN 原生的 `directive` 语法设置
- `%reset` 只清除物质状态，保留宏定义和调试设置（一次性操作）
- `%reset all` 清除所有状态，包括宏定义、调试设置等（一次性操作）
- `%macro reset` 只清除宏定义，不影响物质状态
- 物质状态默认在单元格之间自动累积（默认模式）
- `%preserve`  用于控制哪些物质可以累积（仅对当前单元格有效）

### 模拟器类型

- `Stochastic` - 随机模拟 (Gillespie 算法)
- `Deterministic` - 确定性模拟 (ODE)
- `DeterministicStiff` - 刚性 ODE 求解器
- `JIT` - 即时编译模拟
- `Sundials` - SUNDIALS 求解器
- `SundialsStiff` - 刚性 SUNDIALS 求解器



### 单元格状态传递

#### 默认模式：物质累积

默认情况下，每个单元格执行后的**终态物质浓度**会自动作为下一个单元格的**初始条件**，并且会与当前单元格的声明值**累加**。

```crn
(* Cell 1 *)
| 10 A
| 5 B
A ->{0.1} B
(* 结果：A 和 B 的终态会累积到试管中 *)

(* Cell 2 *)
| 3 A
(* A 的初始值 = 10 (上一单元格终态) + 3 (当前声明) = 13 *)
(* B 的初始值 = 上一单元格终态（未声明，继续保留） *)
```

#### 保留模式：部分物质累积

使用 `%preserve` / `%保留` 指令可以控制哪些物质继续累积，其他物质则重置为当前声明值。

```crn
(* Cell 1 *)
| 10 XP
| 3 XN
(* 结果：XP=10, XN=3 → 试管中累积 *)

(* Cell 2 *)
%preserve XP
| 5 XP
| 2 XN
(* XP 是保留物质：累加 10 (上一单元格) + 5 (当前) = 15 *)
(* XN 未保留：不累加，只使用当前声明值 = 2 *)
(* 实际初始值：XP=15, XN=2 *)
```

**语义解释**：
> `%preserve XP` 的含义是："只有 XP 可以继续累积（上一单元格终态 + 当前声明值），其他物质都重置（只用当前声明值）"

**注意**：
- `%preserve` / `%保留` 仅对当前单元格有效
- 下一个单元格默认恢复为累加模式（除非再次指定）
- 支持中文指令名：`%保留 XP XN`
- 支持英文指令名：`%preserve XP XN`

**使用场景**：
- SGD（随机梯度下降）等需要保留某些状态变量的场景
- 部分物质需要重置，部分物质需要连续累积的实验

### 宏定义与使用

CRNKernel 支持宏定义功能，允许你封装可重用的 CRN 模块。

#### 定义宏

**基本语法：**

```crn
%define ADD(A, B) :(Y)
directive parameters [ k = 0.003 ]
| 10 C
| A ->{k} A + Y
| B ->{k} B + Y
| Y ->{k} C
%end define
```

**带速率参数的宏定义：**

```crn
%define ADD(A, B) :(Y) rate params (k = 0.003, rate2 = 0.01)
directive parameters [ k = k ]
| A ->{k} A + Y
| B ->{rate2} B + Y
%end define
```

- `rate params` 子句是可选的，用于声明可替换的速率参数及其默认值
- 在宏体中，这些参数可以在 `directive parameters` 或反应速率中使用

#### 使用宏

**基本调用：**

```crn
%invoke ADD(Input1, Input2) :(Output)
```

**带实例别名的调用：**

```crn
%invoke ADD(Input1, Input2) :(Output) as add1
```

**带速率参数覆盖的调用：**

```crn
%invoke ADD(Input1, Input2) :(Output) as add1 with rate (k = 0.005)
```

- `as alias`：为宏实例指定唯一别名，用于后续引用内部物质
- `with rate (param = value)`：覆盖宏定义时声明的速率参数

#### 引用宏实例内部物质

使用 `$alias.speciesName` 语法访问宏实例的内部物质：

```crn
%invoke ADD(X, Y) :(Z) as add1

(* 访问 add1 实例的内部物质 Y *)
$add1.Y ->{0.1} Product
```

**嵌套实例引用：**

```crn
%define INNER(A, B) :(Y)
A + B ->{1.0} Y
%end define

%define OUTER(X, Z) :(Result)
    %invoke INNER(X, Z) :(Y) as inner1
    $inner1.Y ->{2.0} Result
%end define

%invoke OUTER(a, b) :(out) as outer1

(* 访问嵌套实例的物质 *)
$outer1.inner1.Y ->{3.0} Final
```

- `$alias.species`：访问顶层实例的物质
- `$parent.child.species`：访问嵌套实例的物质（多层路径）
- 物质名是宏体中声明的原始名称，系统会自动映射到重命名后的实际名称

#### 宏覆盖

当定义相同名称的宏时，新版本会自动覆盖旧版本：

```crn
(* 第一次定义 ADD 宏 *)
%define ADD(A, B) :(Y)
directive parameters [ k = 0.003 ]
| A ->{k} A + Y
| B ->{k} B + Y
%end define

(* 第二次定义 ADD 宏 - 覆盖旧版本 *)
%define ADD(A, B) :(Y)
directive parameters [ k = 0.01 ]
| A + B ->{k} Y
%end define

(* 使用新版本的 ADD 宏 *)
%invoke ADD(X1, X2) :(Result)
```

#### 查看宏定义

```crn
%macro list
```

#### 重置宏注册表

```crn
%macro reset
```

#### 显示展开后的代码

使用 `%export expanded` 可以查看宏展开后的完整代码：

```crn
%export expanded
%invoke ADD(Input1, Input2) :(Output) as add1 with rate (k = 0.005)
```

输出示例：

```
// Registered Macros:
//   ADD(A, B) :(Y) [rate params: k=0.003000]
//     OK

// Full Expanded Code:
directive simulation { ... }
...
// === BEGIN MACRO EXPANSION: ADD_0 (ADD) ===
| 10 ADD_0_local_C
| Input1 ->{0.005} Input1 + Output
| Input2 ->{0.005} Input2 + Output
| Output ->{0.003} ADD_0_local_C
// === END MACRO EXPANSION: ADD_0 ===

// Macro Instances:
//   ADD_0 (alias: add1):
//     Inputs: map [("A", "Input1"); ("B", "Input2")]
//     Outputs: map [("Y", "Output")]
//     Rate Params: [("k", 0.005)]
//     Local Species Mapping: [("C", "ADD_0_local_C")]
```

**说明：**
- 局部物质被重命名为 `MacroName_InstanceID_local_OriginalName` 格式
- 输入/输出参数保持调用者提供的名称
- 速率参数显示合并后的值（默认值被覆盖值替换）
- 如果提供了别名，会显示在实例信息中

#### 调试输出

使用 `%debug on` 启用调试输出，会显示模拟参数、物质浓度等详细信息：

```crn
%debug on
directive simulation {final=100.0; points=100}
| 10 A
| 0 B
A ->{0.1} B
```

使用 `%debug off` 禁用调试输出（默认状态）。

## 输出格式

每个单元格执行后会显示：

1. **交互式图表** - 使用 Plotly 显示物质浓度随时间变化
2. **Markdown 表格** - 终态浓度汇总
3. **文本输出** - 模拟统计信息
4. **调试输出**（如果启用 `%debug on`）- 显示模拟参数、物质浓度等详细信息
5. **展开的代码**（如果使用 `%export expanded`）- 显示宏展开后的完整代码

## 项目结构

```
CRNKernel/
├── AssemblyInfo.fs       # 程序集信息
├── JupyterProtocol.fs    # Jupyter 消息协议
├── KernelState.fs        # 内核状态管理
├── MacroProcessor.fs     # 宏处理器（宏定义、展开）
├── Preprocessor.fs       # 预处理指令解析
├── CodeParser.fs         # 代码解析器 (CRN/DSD)
├── ExecutionEngine.fs    # 执行引擎
├── Visualization.fs      # 可视化输出
├── ErrorHandling.fs      # 错误处理
├── Kernel.fs             # Jupyter 内核实现
├── Program.fs            # 程序入口
├── kernel.json           # Jupyter 内核配置
├── logo-64x64.svg        # 内核图标
├── install-kernel.bat    # Windows 安装脚本
├── install-kernel.sh     # Linux/macOS 安装脚本
├── README.md             # 本文档
├── INSTALL.md            # 详细安装指南
└── examples/             # 示例 Notebook
    ├── BasicCRN.ipynb          # 基础 CRN 示例
    ├── LotkaVolterra.ipynb     # Lotka-Volterra 模型
    ├── DSDBasic.ipynb          # DSD 基础示例
    ├── DSDExample.ipynb        # DSD 完整示例
    ├── Directives.ipynb        # 指令系统演示
    ├── DebugTest.ipynb         # 调试功能测试
    └── Macros.ipynb            # 宏功能演示
```

## 故障排除

### 内核未显示

1. 确认已运行安装脚本
2. 检查 `jupyter kernelspec list` 输出
3. 重启 Jupyter Notebook

### 模拟失败

1. 检查语法是否正确（使用正确的 CRN/DSD 语法）
2. 查看错误消息中的详细信息
3. 确保所有物质都已正确初始化

### 图表不显示

1. 确保 Jupyter Notebook 支持 Plotly
2. 尝试在浏览器中打开 Notebook
3. 检查浏览器控制台是否有错误

### 宏未生效

1. 确认宏定义语法正确（使用 `%define ... %end define`）
2. 确认宏调用语法正确（使用 `%invoke ...`）
3. 使用 `%macro list` 检查宏是否已注册

## 贡献

欢迎贡献代码！请查看主项目的贡献指南。

## 许可证

MIT License - 与主项目保持一致

## 相关链接

- [CRN Engine GitHub](https://github.com/microsoft/CRN)
- [Visual DSD](https://ph1ll1ps.github.io/project/visualdsd/)
- [Jupyter Documentation](https://jupyter.org/documentation)
