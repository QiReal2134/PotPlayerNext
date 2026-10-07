# GitHub 自动构建与发布

作者：qireal。代码采用 GPL-3.0-only。

仓库：https://github.com/QiReal2134/PotPlayerNext

WinUI NuGet依赖记录于packages.lock.json，构建使用locked restore，避免依赖在开发机和CI间悄悄改变。

## 触发规则

- 分支提交、Pull Request：Windows 2022 构建、Rust 单元测试/格式/Clippy、实际 Rust/C# ABI、生产逻辑检查；构建 WinUI、MSI、便携包和 GPL 源码归档。产物保留14天，不自动变成正式 Release。
- 推送 `vX.Y.Z` 标签：在上述步骤及 MSI 只读资源/关联检查成功后，自动创建 GitHub Release。
- Actions → Windows build and release → Run workflow：默认仅构建。勾选 publish 时，必须有与当前构建提交完全一致的版本标签。
- 任一步失败不会公开新版本。上传失败只保留草稿；重新运行可补齐草稿，已公开的 Release 不被覆盖。

版本以 `src/PotPlayerNext/PotPlayerNext.csproj` 的 Version 为单一来源；标签必须精确匹配。当前安装器支持三段数字版本，不接受 prerelease 后缀。v0.2.6 已完成实际标签构建和公开发布；当前 0.2.7 为本地修复候选，尚未推送或发布。

## 发布新版本

1. 修改 csproj 中的 Version，例如 `0.2.7`，提交源码改动。
2. 推送提交和同名标签：

```powershell
git add src/PotPlayerNext/PotPlayerNext.csproj
git commit -m "chore: release 0.2.7"
git push origin main
git tag -a v0.2.7 -m "PotPlayerNext 0.2.7"
git push origin v0.2.7
```

3. 在 Actions 查看构建。成功后 Releases 自动出现 MSI、portable.zip、source.zip、各自 SHA256、SHA256SUMS.txt 和 release-manifest.json（版本、提交、文件哈希）。便携包包含自包含 .NET / WinUI / Rust，解压后运行；不会自动注册登录启动或文件关联。

## 权限与验收范围

工作流默认 contents:read，只有通过构建后的 release job 使用 contents:write。使用 GitHub 自动提供的 GITHUB_TOKEN，无需把 PAT、账号密码、开发机凭据放进仓库。PR 不进入发布 job；第三方 Actions 固定到官方提交 SHA。

.NET SDK 固定8.0.425，Rust CI固定1.99.0，WiX固定6.0.2。Windows hosted runner必须提供MSVC与Windows SDK，缺失时构建明确失败。SHA256校验不是代码签名；当前MSI未配发行证书。

CI不运行GUI输入注入，也不把编译、ABI或MSI数据库检查视为动画观感、Explorer空格、默认选择、最新包维护或干净机部署的完成证明。发布前仍应阅读 docs/RELEASE-CHECKLIST.md 的实窗待测项。
