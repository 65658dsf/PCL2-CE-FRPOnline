# FRP 提供商扩展指南

## 概览
- 统一接口：`IFrpProvider`，封装认证、节点推荐、隧道创建/启动、停止与清理。
- 工厂：`FrpProviderFactory.Get(key)` 返回指定提供商实例（`stellar`、`local`）。
- 持久化：`Config.Link.FrpProvider` 保存当前选择，默认 `local`。
- UI 入口：设置页登录卡下的“服务商”下拉框（`ComboFrpProvider`）。

## 标准接口
```vb
Public Interface IFrpProvider
    Function AuthenticateAsync() As Task(Of Boolean)
    Function GetSuggestedNodesAsync() As Task(Of List(Of ProviderNode))
    Function CreateOrStartTunnelAsync(localPort As Integer, username As String, Optional lobbyCode As String = "") As Task(Of Tuple(Of Boolean, String))
    Function StopTunnelAsync() As Task
    Function DeleteTunnelIfNeededAsync() As Task
    Function GetPublicAddress() As String
End Interface
```

### ProviderNode
```vb
Public Class ProviderNode
    Public Property Id As Integer
    Public Property Name As String
    Public Property Region As String
End Class
```

## 注册与选择
```vb
Public Module FrpProviderFactory
    Public Function Get(name As String) As IFrpProvider
        Dim key = If(String.IsNullOrWhiteSpace(name), "local", name.ToLowerInvariant())
        Select Case key
            Case "stellar" : Return New StellarFrpProvider()
            Case Else : Return New LocalFrpProvider()
        End Select
    End Function
End Module
```

## 示例：新增 MyFrpProvider
```vb
Public Class MyFrpProvider
    Implements IFrpProvider

    Public Async Function AuthenticateAsync() As Task(Of Boolean) Implements IFrpProvider.AuthenticateAsync
        ' 执行授权流程（OAuth 或 API Key）
        Return True
    End Function

    Public Async Function GetSuggestedNodesAsync() As Task(Of List(Of ProviderNode)) Implements IFrpProvider.GetSuggestedNodesAsync
        ' 返回推荐节点列表
        Return New List(Of ProviderNode)()
    End Function

    Public Async Function CreateOrStartTunnelAsync(localPort As Integer, username As String, Optional lobbyCode As String = "") As Task(Of Tuple(Of Boolean, String)) Implements IFrpProvider.CreateOrStartTunnelAsync
        ' 创建并启动隧道，返回公网直连地址
        Return New Tuple(Of Boolean, String)(True, "host:port")
    End Function

    Public Async Function StopTunnelAsync() As Task Implements IFrpProvider.StopTunnelAsync
        ' 停止隧道
    End Function

    Public Async Function DeleteTunnelIfNeededAsync() As Task Implements IFrpProvider.DeleteTunnelIfNeededAsync
        ' 按需清理已创建隧道
    End Function

    Public Function GetPublicAddress() As String Implements IFrpProvider.GetPublicAddress
        Return Nothing
    End Function
End Class
```

在 `FrpProviderFactory.Get` 中增加：
```vb
Case "myfrp" : Return New MyFrpProvider()
```

## UI 绑定约定
- 设置页：选择框写入 `Config.Link.FrpProvider`，触发 `Reload()` 更新卡片与文案。
- 大厅创建：读取 `Config.Link.FrpProvider` 并通过 Provider 调用创建/启动隧道，刷新 `LabConnectType` 显示提供商类型。

## 测试与兼容
- 单元测试建议：
  - 配置持久化读写
  - 工厂选择正确 Provider
  - 模板替换（如用户名/房间号）与端口范围解析
- 浏览器兼容性（OAuth）：Edge/Chrome/Firefox 下授权回调均应写入令牌并更新 UI。

## 常见问题
- 授权失败：检查回调参数与本地监听端口是否被占用。
- 日志规范：使用前缀标签（如 `[MyFrp]`）便于问题定位。