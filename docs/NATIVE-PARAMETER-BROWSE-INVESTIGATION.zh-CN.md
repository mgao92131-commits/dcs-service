# 模块参数枚举接口调查

调查对象：H:\share\bin 中 DeltaV 10.3.4.5354 的本地文件。仅静态解析 PE 导出、反汇编和 .NET ReflectionOnly 元数据；没有执行厂商原生调用，没有修改服务。

目标路径 PICA-117024/PID1/LO_LIM.CV 的层级是：模块 PICA-117024、功能块 PID1、参数/属性 LO_LIM、字段 CV。

## 已确认的 bss.dll 导出

| 接口 | 本批二进制静态 VA | 作用 |
|---|---|---|
| RtBlock::blocksListOnPath(RtStringList&, RtPath&) | 0x20c176c0 | 按路径找块并列出子块 |
| RtBlock::attributesListOnPath(RtStringList&, RtPath&) | 0x20c17700 | 按路径找块并列出属性 |
| RtBlock::blocksList(RtStringList&) | 0x20c172f0 | 当前块下的子块列表 |
| RtBlock::attributesList(RtStringList&) | 0x20c171d0 | 当前块下的属性列表 |
| RtBlock::attributeNameAt(wchar_t*&, int) | 0x20c02ab0 | 按索引取属性名（具体派生类型可能覆盖） |
| RtBlock::findAttributeDataType | 多个重载 | 属性类型查询 |
| DataTypeInfo::NumFields(DATA_TYPES) | 0x20c010a0 | 类型字段数 |
| DataTypeInfo::FieldName(DATA_TYPES, int) | 0x20c01440 | 类型字段名 |
| DataTypeInfo::FieldNames(RtStringList&, DATA_TYPES) | 0x20c21250 | 类型字段列表 |

VA 只用于本次静态证据记录，不应直接硬编码为调用地址。正式代码仍须解析符号并核实实际对象派发。

## 远程运行时限制

blocksListOnPath 调用虚表 +0x208 找块，再调用找到对象的 +0xe0；attributesListOnPath 对应 +0xe4。基础实现读取本地子块/属性容器，不是直接发起一次远程控制器枚举。

已检查的 RtSecureModule 和 RtModuleProxyC 的 findComponent 虚表 +0xd0 实现分别落在 Sec.dll 0x20a4a0c0 和 roc.dll 0x2261ea70，都是将输出设为 null、返回状态 1 的桩函数。它们的列表函数沿用基础 RtBlock 实现。因此，虽然导出接口真实存在，尚不能确认当前实时读取使用的角色可以枚举 PICA-117024 的远程子块或属性；空列表也不能当作模块没有参数。

rtc.dll 另有 RtcElementalFunctionBlock、RtcCompositeFunctionBlock、RtcCompositeControlModule 的 attributeNameAt 覆盖，及 RtcElementalFunctionBlock::attributesList，说明实际运行时块类型支持属性枚举，但如何从操作站取得相应对象仍需验证。

## 配置库元数据接口（非 OPC）

ReflectionOnly 检查 DeltaV.Config.Db.dll 确认：
- DeltaV.Config.Db.Base.BlockBase.get_blocks() 返回 AssociationRole。
- 同类 get_attributes() 返回 AssociationRole。
- AttributeBase.get_type()、get_definition() 可提供类型和定义信息。
- ModuleInstance 以及 ModuleBase 存在，可表达模块实例。

这是配置对象模型，适合后续核实模块结构和参数定义。配置定义与当前下载到控制器、允许读取的运行时参数不能直接等同；连接方式、用户权限、查找模块、关联展开和字段展开尚未现场验证。

## 当前结论

DLL 内存在块/参数/字段枚举相关 API。尚未取得 PICA-117024 的完整参数列表。现有服务只有实时值读取接口，没有模块浏览 API；后续应先用只读现场探针验证当前运行时链，若不支持，再验证配置库的模块结构查询，再决定服务接口。不得先猜参数名称或发布未验证的空列表。

原始元数据：.work/module-browse-investigation/config-metadata.txt。
