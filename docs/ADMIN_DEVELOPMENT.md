# 管理端开发热更新

在项目根目录运行：

```bash
./start-admin.sh --watch
```

三个 `start-server.sh`、`start-admin.sh`、`start-client.sh` 开发入口统一使用 Debug，设置 Development 环境；`--watch` 仍只用于管理端热更新。保留当前所选版本、管理地址、独立 Cookie、密钥、受管 PID 和实例归属检查。`publish.sh` 及发布程序仍使用 Release。

仅 Debug 构建且 Development 环境的管理端允许本机免密，直接访问首页；`/login` 自动跳回首页。仍校验回环客户端、回环 Host、同源 Origin 和跨站请求，保留表单防伪、服务端管理密钥及版本匹配。Release 即使设为 Development 也需要密钥登录；Debug 的 Production 也需要登录。不要将开发免密入口代理到远程使用。

支持的 Razor/C# 修改热更新，CSS由浏览器自动刷新；不支持热更新的结构修改由 `dotnet watch --non-interactive` 自动重启管理端。编译错误在终端显示，旧应用仍运行，修复后继续处理；不会因改管理界面而自动重启游戏服务端或迁移数据库。

监听地址、认证、依赖注入等已经执行的启动逻辑，不保证应用热更新后重新执行；遇到这种修改，停止并重新运行入口。修改Shared管理契约时，仍需构建匹配的服务端；管理端watch不能让旧服务端自动支持新API。

开发 watcher 的管理进程重启后，旧 Blazor 会话可能失效；仅在 Development 且 DOTNET_WATCH=1 时启用开发重连脚本，最多30次每秒尝试恢复，服务端拒绝旧会话时自动重新加载页面，恢复失败显示原重连提示。普通运行和发布模式不启用自动刷新。自动重启/刷新可能丢失未保存表单草稿，重要修改先保存。

退出 watcher 使用Ctrl+C；再次运行普通 `./start-admin.sh` 也会按实例归属停止旧watcher及其应用，再启动普通管理端。只处理同一版本runtime的管理端，不处理其他版本或游戏服。启动脚本先完成构建，构建失败不停止旧实例。

本机SDK的默认中间目录曾包含反斜杠，导致Unix watcher无法读取静态资源清单；Admin项目显式使用`obj/$(Configuration)/`并让SDK追加目标框架，保持原输出结构。

## 隔离验证

```bash
NODE_PATH=/path/to/installed/node_modules python3 tests/Management/watch.py
```

需已安装Playwright/Chromium，可用MIR2_BROWSER_EXECUTABLE指定浏览器。测试在项目中短暂创建唯一名称的专用Razor/CSS/C#探针，使用临时版本、独立端口和进程组；结束后删除探针。运行时不要同时执行另一轮watch测试，避免探针增删相互触发编译。正式版本不会被测试写入或重启。
