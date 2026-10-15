# 第三方组件与许可

本仓库自有源代码使用 MIT 许可证。没有捆绑网页、字体、OCR 模型或用户截图。

| 组件 | 使用方式 | 许可 / 来源 |
| --- | --- | --- |
| .NET 8 runtime | Windows 自包含发行包内 | MIT；[dotnet/runtime LICENSE](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)，[第三方声明](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT) |
| WPF / Windows Forms | GUI、托盘和显示器枚举 | MIT；[dotnet/wpf](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)、[dotnet/winforms](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT) |
| C#/WinRT 与 Windows SDK .NET 投影 | 编译目标框架带入，调用 Windows OCR | MIT；[CsWinRT](https://github.com/microsoft/CsWinRT/blob/master/LICENSE)、[Windows SDK .NET](https://www.nuget.org/packages/Microsoft.Windows.SDK.NET.Ref/) |
| Windows OCR / GDI / User32 | 用户操作系统提供，未打包 OCR 模型 | 适用 Windows 自身许可；[Windows.Media.Ocr](https://learn.microsoft.com/uwp/api/windows.media.ocr.ocrengine) |

构建脚本将 SDK 自包含发布目录中的 LICENSE / THIRD-PARTY-NOTICES 文件一并保留，并附带本文件。Windows OCR 所需语言由用户的 Windows 安装管理。无需上传图像，也无需模型/API 密钥。
