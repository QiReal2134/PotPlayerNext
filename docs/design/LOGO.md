# PotPlayerNext · qireal

用户选定的参考：approved-logo-reference.png。实现采用天蓝圆盘、白色播放三角形、右下标白色 N；不使用图片背景或栅格文字。SVG / ICO / WinUI Path 保持相同 256 单位几何。

主界面冷启动开屏：圆盘轻缩放淡入 → 三角形淡入展开 → 下标 N 轻移淡入，850ms 后打开主窗口；显示 qireal 署名。使用原生 WinUI Storyboard，只动画 Opacity / RenderTransform，不引入 GIF、视频解码或额外动画依赖。系统关闭动画时跳过。

文件直开、Space 快捷预览、--background、--stop-background 不进入开屏路径；预览仍保持无文字的纯媒体窗口。开屏跟随系统深浅色，使用现有 DesktopAcrylicBackdrop。
