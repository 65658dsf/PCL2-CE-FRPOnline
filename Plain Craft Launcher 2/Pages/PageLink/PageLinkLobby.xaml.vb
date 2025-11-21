Imports System.Collections.ObjectModel
Imports System.Collections.Specialized
Imports PCL.Core.App
Imports PCL.Core.Link
Imports PCL.Core.Link.EasyTier
Imports PCL.Core.Link.Lobby

Public Class PageLinkLobby
    Private _currentPublicAddress As String = Nothing
    Private _pingCts As CancellationTokenSource = Nothing
    Private Function GetUsername() As String
        Dim name = Setup.Get("LinkUsername")
        If String.IsNullOrWhiteSpace(name) AndAlso SelectedProfile IsNot Nothing Then name = SelectedProfile.Username
        If String.IsNullOrWhiteSpace(name) Then name = "Player"
        Return name
    End Function

#Region "初始化"

    '加载器初始化
    Private Sub LoaderInit() Handles Me.Initialized
        PageLoaderInit(Load, PanLoad, PanContent, Nothing, InitLoader, AutoRun:=False)
        AddHandler InitLoader.OnStateChangedUi, AddressOf OnLoadStateChanged
        AddHandler LobbyService.DiscoveredWorlds.CollectionChanged, AddressOf OnDiscoveredWorldsChanged
    End Sub

    


    Public Async Sub Reload() Handles Me.Loaded
        HintAnnounce.Visibility = Visibility.Collapsed
        Await LobbyService.InitializeAsync().ConfigureAwait(False)
        Try
            Await LobbyService.DiscoverWorldAsync().ConfigureAwait(False)
        Catch
        End Try
    End Sub

    Private Sub OnDiscoveredWorldsChanged(sender As Object, e As NotifyCollectionChangedEventArgs)
        RunInUi(Sub()
                    If e.Action = NotifyCollectionChangedAction.Reset Then
                        ComboWorldList.Items.Clear()
                        For Each world As FoundWorld In LobbyService.DiscoveredWorlds
                            ComboWorldList.Items.Add(New MyComboBoxItem() With {
                                .Tag = world.Port,
                                .Content = world.Name
                            })
                        Next
                    End If

                    If e.NewItems IsNot Nothing Then
                        For Each world As FoundWorld In e.NewItems
                            ComboWorldList.Items.Add(New MyComboBoxItem() With {
                                .Tag = world.Port,
                                .Content = world.Name
                            })
                        Next
                    End If

                    If e.OldItems IsNot Nothing Then
                        Dim portsToRemove = e.OldItems.Cast(Of FoundWorld)().Select(Function(w) w.Port).ToHashSet()
                        Dim itemsToRemove = ComboWorldList.Items.Cast(Of MyComboBoxItem)().Where(Function(item) portsToRemove.Contains(CType(item.Tag, Integer))).ToList()
                        For Each item In itemsToRemove
                            ComboWorldList.Items.Remove(item)
                        Next
                    End If

                    Dim hasItems = ComboWorldList.Items.Count > 0
                    ComboWorldList.IsEnabled = hasItems
                    BtnCreate.IsEnabled = hasItems
                    If hasItems AndAlso ComboWorldList.SelectedIndex = -1 Then
                        ComboWorldList.SelectedIndex = 0
                    End If
                End Sub)
    End Sub

#End Region

#Region "加载步骤"

    Private Shared WithEvents InitLoader As New LoaderCombo(Of Integer)("房间初始化", {
        New LoaderTask(Of Integer, Integer)("初始化", AddressOf InitTask) With {.ProgressWeight = 0.5}
    })
    Private Shared Async Sub InitTask(task As LoaderTask(Of Integer, Integer))
        Await LobbyService.InitializeAsync()
    End Sub

#End Region

#Region "PanSelect | 种类选择页面"

    '刷新按钮
    Private Async Sub BtnRefresh_Click(sender As Object, e As EventArgs) Handles BtnRefresh.Click
        Try
            Await LobbyService.DiscoverWorldAsync().ConfigureAwait(False)
        Catch
        End Try
        RunInUi(Sub()
                    ComboWorldList.Items.Clear()
                    For Each world As FoundWorld In LobbyService.DiscoveredWorlds
                        ComboWorldList.Items.Add(New MyComboBoxItem() With {
                            .Tag = world.Port,
                            .Content = world.Name
                        })
                    Next
                    Dim hasItems = ComboWorldList.Items.Count > 0
                    ComboWorldList.IsEnabled = hasItems
                    BtnCreate.IsEnabled = hasItems
                    If hasItems AndAlso ComboWorldList.SelectedIndex = -1 Then
                        ComboWorldList.SelectedIndex = 0
                    End If
                End Sub)
    End Sub
    
    '创建房间
    Private Async Sub BtnCreate_Click(sender As Object, e As EventArgs) Handles BtnCreate.Click
        If ComboWorldList.SelectedItem Is Nothing Then
            Hint("请先选择一个要联机的世界！", HintType.Info)
            Return
        End If

        BtnCreate.IsEnabled = False

        Dim port = CType(ComboWorldList.SelectedItem.Tag, Integer)
        Log("[Link] 创建房间，端口：" & port)
        Dim username = GetUsername()

        RunInUi(Sub()
                    BtnFinishPing.Visibility = Visibility.Collapsed
                    LabFinishPing.Text = "-ms"
                    BtnConnectType.Visibility = Visibility.Collapsed
                    LabConnectType.Text = "连接中"
                    LabConnectUserName.Text = username
                    LabConnectUserType.Text = "创建者"
                    LabFinishId.Text = ""
                    BtnFinishCopyIp.Visibility = Visibility.Collapsed
                    BtnCreate.IsEnabled = True
                    BtnFinishExit.Text = "关闭房间"
                    CurrentSubpage = Subpages.PanFinish
                End Sub)

        Dim providerKey = Config.Link.FrpProvider
        System.Threading.Tasks.Task.Run(Async Function()
                             Dim provider = FrpProviderFactory.GetProvider(providerKey)
                             Dim resTuple = Await provider.CreateOrStartTunnelAsync(port, username)
                             If resTuple.Item1 Then
                                 _currentPublicAddress = resTuple.Item2
                                 Dim typeText = If(String.Equals(providerKey, "stellar", StringComparison.OrdinalIgnoreCase), "Stellar 中心转发", "本地 FRP")
                                 RunInUi(Sub()
                                         LabConnectType.Text = typeText
                                         LabFinishQuality.Text = "已连接"
                                         LabFinishId.Text = _currentPublicAddress
                                         BtnFinishCopyIp.Visibility = Visibility.Visible
                                     End Sub)
                                 StartRoomMonitoring()
                             Else
                                 RunInUi(Sub() Hint("隧道启动失败，请稍后重试", HintType.Critical))
                             End If
                         End Function)
        '房间创建已在隧道任务中异步发起
    End Sub

    

#End Region

#Region "PanLoad | 加载中页面"

    '承接状态切换的 UI 改变
    Private Sub OnLoadStateChanged(loader As LoaderBase, newState As LoadState, oldState As LoadState)
    End Sub
    Private Shared _loadStep As String = "准备初始化"
    Private Shared Sub SetLoadDesc(intro As String, [step] As String)
        Log("连接步骤：" & intro)
        _loadStep = [step]
        RunInUiWait(Sub()
                        If FrmLinkLobby Is Nothing OrElse Not FrmLinkLobby.LabLoadDesc.IsLoaded Then Exit Sub
                        FrmLinkLobby.LabLoadDesc.Text = intro
                        FrmLinkLobby.UpdateProgress()
                    End Sub)
    End Sub

    '承接重试
    Private Sub CardLoad_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles CardLoad.MouseLeftButtonUp
        If Not InitLoader.State = LoadState.Failed Then Exit Sub
        InitLoader.Start(IsForceRestart:=True)
    End Sub

    '取消加载
    Private Sub CancelLoad() Handles BtnLoadCancel.Click
        If InitLoader.State = LoadState.Loading Then
            CurrentSubpage = Subpages.PanSelect
            InitLoader.Abort()
        Else
            InitLoader.State = LoadState.Waiting
        End If
    End Sub

    '进度改变
    Private Sub UpdateProgress(Optional value As Double = -1)
        If value = -1 Then value = InitLoader.Progress
        Dim displayingProgress As Double = ColumnProgressA.Width.Value
        If Math.Round(value - displayingProgress, 3) = 0 Then Exit Sub
        If displayingProgress > value Then
            ColumnProgressA.Width = New GridLength(value, GridUnitType.Star)
            ColumnProgressB.Width = New GridLength(1 - value, GridUnitType.Star)
            AniStop("LobbyController Progress")
        Else
            Dim newProgress As Double = If(value = 1, 1, (value - displayingProgress) * 0.2 + displayingProgress)
            AniStart({
                AaGridLengthWidth(ColumnProgressA, newProgress - ColumnProgressA.Width.Value, 300, Ease:=New AniEaseOutFluent),
                AaGridLengthWidth(ColumnProgressB, (1 - newProgress) - ColumnProgressB.Width.Value, 300, Ease:=New AniEaseOutFluent)
            }, "LobbyController Progress")
        End If
    End Sub
    Private Sub CardResized() Handles CardLoad.SizeChanged
        RectProgressClip.Rect = New Rect(0, 0, CardLoad.ActualWidth, 12)
    End Sub

#End Region

#Region "PanFinish | 加载完成页面"
    '退出
    Private Async Sub BtnFinishExit_Click(sender As Object, e As EventArgs) Handles BtnFinishExit.Click
        If MyMsgBox("你确定要关闭房间吗？", "确认关闭", "确定", "取消", IsWarn:=True) = 1 Then
            Try
                If _pingCts IsNot Nothing Then
                    _pingCts.Cancel()
                    _pingCts.Dispose()
                    _pingCts = Nothing
                End If
            Catch
            End Try
            CurrentSubpage = Subpages.PanSelect
            BtnFinishExit.Text = "关闭房间"
            Await FrpController.StopAsync().ConfigureAwait(True)
            Try
                Dim providerKey = Config.Link.FrpProvider
                Dim provider = FrpProviderFactory.GetProvider(providerKey)
                Await provider.DeleteTunnelIfNeededAsync()
            Catch
            End Try
        End If
End Sub

    Private Sub StartRoomMonitoring()
        Try
            If _pingCts IsNot Nothing Then
                _pingCts.Cancel()
                _pingCts.Dispose()
            End If
            _pingCts = New CancellationTokenSource()
            Dim token As CancellationToken = _pingCts.Token

            System.Threading.Tasks.Task.Run(Async Function()
                                                While Not token.IsCancellationRequested
                                                    Try
                                                        Dim host As String = Nothing
                                                        Dim port As Integer = 25565
                                                        If Not String.IsNullOrWhiteSpace(_currentPublicAddress) AndAlso _currentPublicAddress.Contains(":") Then
                                                            Dim parts = _currentPublicAddress.Split(":"c)
                                                            host = parts(0)
                                                            Integer.TryParse(parts(1), port)
                                                        Else
                                                            host = _currentPublicAddress
                                                        End If
                                                        If String.IsNullOrWhiteSpace(host) Then GoTo DelayNext

                                                        Using query As New McPing(host, port)
                                                            Dim result As McPingResult = Nothing
                                                            Try
                                                                result = Await query.PingAsync(token)
                                                            Catch
                                                            End Try
                                                            If result IsNot Nothing Then
                                                                RunInUi(Sub()
                                                                            LabFinishQuality.Text = "已连接 " & result.Latency & "ms"
                                                                            CardPlayerList.Title = "房间成员列表（共 " & result.Players.Online & " 人）"
                                                                            StackPlayerList.Children.Clear()
                                                                            If result.Players.Samples IsNot Nothing AndAlso result.Players.Samples.Count > 0 Then
                                                                                For Each p In result.Players.Samples
                                                                                    Dim item As New MyListItem With {
                                                                                        .Title = p.Name,
                                                                                        .Info = "",
                                                                                        .Type = MyListItem.CheckType.None,
                                                                                        .Tag = p
                                                                                    }
                                                                                    StackPlayerList.Children.Add(item)
                                                                                Next
                                                                            End If
                                                                        End Sub)
                                                            End If
                                                        End Using
                                                    Catch
                                                    End Try
DelayNext:
                                                    Try
                                                        Await System.Threading.Tasks.Task.Delay(5000, token)
                                                    Catch
                                                    End Try
                                                End While
                                            End Function, token)
        Catch
        End Try
    End Sub

    '复制房间标识
    Private Sub BtnFinishCopy_Click(sender As Object, e As EventArgs) Handles BtnFinishCopy.Click
        ClipboardSet(LabFinishId.Text)
    End Sub

    '复制 IP
    Private Sub BtnFinishCopyIp_Click(sender As Object, e As EventArgs) Handles BtnFinishCopyIp.Click
        Dim ip As String = _currentPublicAddress
        If String.IsNullOrWhiteSpace(ip) Then ip = FrpController.PublicHost & ":" & FrpController.PublicPort.ToString()
        MyMsgBox("公网直连地址：" & ip & vbCrLf & "请在 MC 的多人-直接连接中输入该地址加入。", "复制地址",
                 Button1:="复制", Button2:="返回", Button1Action:=Sub() ClipboardSet(ip))
    End Sub

#End Region

#Region "子页面管理"

    Public Enum Subpages
        PanSelect
        PanFinish
    End Enum
    Private _CurrentSubpage As Subpages = Subpages.PanSelect
    Public Property CurrentSubpage As Subpages
        Get
            Return _CurrentSubpage
        End Get
        Set(value As Subpages)
            If _CurrentSubpage = value Then Exit Property
            _CurrentSubpage = value
            Log("[Link] 子页面更改为 " & GetStringFromEnum(value))
            PageOnContentExit()
        End Set
    End Property

    Private Sub PageLinkLobby_OnPageEnter() Handles Me.PageEnter
        FrmLinkLobby.PanSelect.Visibility = If(CurrentSubpage = Subpages.PanSelect, Visibility.Visible, Visibility.Collapsed)
        FrmLinkLobby.PanFinish.Visibility = If(CurrentSubpage = Subpages.PanFinish, Visibility.Visible, Visibility.Collapsed)
    End Sub

#End Region

End Class
