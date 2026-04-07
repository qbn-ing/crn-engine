# CRNKernel - Jupyter Kernel for Chemical Reaction Networks

CRNKernel 是一个 Jupyter Notebook 内核，用于编程和分析生化系统。

## 快速开始

### 1. 安装内核

**安装到系统 Jupyter:**
```bash
install.bat
```

**安装到 Conda 环境:**
```bash
install.bat --conda crn-test
```

### 2. 启动 Jupyter

```bash
jupyter notebook
```

### 3. 创建 CRN Notebook

在新建笔记本时选择 "CRN (F#)" 内核。

## 示例代码

### 简单反应

```crn
directive simulation {final=100.0; points=100}
directive parameters [ k = 0.01 ]

| 10 A
| 10 B
| 0 C

A + B ->{k} C
```

### 使用宏

```crn
(* 定义宏 *)
%define ADD(A, B) :(Y)
directive parameters [ k = 0.003 ]
| A ->{k} A + Y
| B ->{k} B + Y
%end define

(* 使用宏 *)
%invoke ADD(Input1, Input2) :(Output)
```

## 可用指令

| 指令 | 描述 |
|------|------|
| `%reset` | 重置物质状态（保留宏定义） |
| `%reset all` | 重置所有状态（包括宏定义） |
| `%debug on/off` | 启用/禁用调试输出 |
| `%export expanded` | 显示宏展开后的代码 |
| `%macro list` | 列出所有宏定义 |
| `%macro reset` | 清除所有宏定义 |

## 功能特性

- ✅ 支持 CRN/DSD 语言编程
- ✅ 单元格之间自动传递物质浓度状态
- ✅ 支持多种模拟器（随机、确定性等）
- ✅ 实时显示物质浓度变化图表
- ✅ 支持宏定义和复用

## 更多信息

完整文档请参阅主项目的 README.md。

## 相关链接

- [CRN Engine GitHub](https://github.com/microsoft/CRN)
- [Visual DSD](https://ph1ll1ps.github.io/project/visualdsd/)
