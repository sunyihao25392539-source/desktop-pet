# 金丝猴桌面宠物 macOS 版

这是 Windows v14 的原生 macOS 移植版，支持 macOS 13 及以上版本。

## 已迁移功能

- 透明、无边框、始终置顶的桌面宠物窗口
- 随机走动、左右转身、屏幕边缘约束和停止/恢复走动
- 眨眼、待机动作、坐着睡觉、呼吸、叫醒和拖动回弹
- 喂樱桃、爱心留言、摸头、双击互动和漂浮爱心
- 右键菜单
- Codex 忙碌、执行工具、修改文件、等待确认、结束和中断状态气泡
- Hook 通过跨进程通知即时刷新，200 毫秒文件轮询作为兜底；遗失结束事件的旧忙碌状态会自动回到空闲
- 在右键菜单中一键安装 macOS Codex Hooks

## 下载和打开

在 GitHub 仓库的 Actions 页面打开最新一次 `Build macOS app`，下载：

- M1、M2、M3、M4、M5 Mac：`GoldenMonkeyPet-macOS-AppleSilicon`
- Intel Mac：`GoldenMonkeyPet-macOS-Intel`

解压后将 `GoldenMonkeyPet.app` 拖进“应用程序”。当前自动构建使用临时签名，第一次打开时如果 macOS 阻止运行，请在 Finder 中按住 Control 点击应用，选择“打开”，再确认一次。

要显示 Codex 工作状态，请右键金丝猴，选择“安装 Codex 联动”，然后在 Codex 的 Hooks 设置里审核并信任新 Hook。移动应用到其他目录后，应重新执行一次安装。

## 在 Mac 上本地构建

安装 Xcode Command Line Tools 后运行：

```bash
chmod +x macos/build-app.sh
macos/build-app.sh
```

生成文件位于 `macos/dist/`。

## 正式分发说明

GitHub Actions 目前进行 ad-hoc 临时签名，适合测试和朋友之间分发。要做到双击直接打开且不出现开发者警告，需要 Apple Developer ID 证书和 Apple notarization，并在工作流中配置相应密钥。
