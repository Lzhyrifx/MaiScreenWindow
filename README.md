# MaiScreenWindow

解决maimai屏幕被外壳挡住时，鼠标点不到目标的问题:\
运行此程序后，鼠标在**有效区外**或者按住**Alt**会自动浮出小窗\
小窗跟随鼠标并把鼠标周围显示出来\
可使用快捷键Ctrl+Alt+O进行固定（固定时始终显示）

## 运行

下载并安装[.NET8.0](https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe)\
在[releases](https://github.com/Lzhyrifx/MaiScreenWindow/releases)下载zip文件解压后运行

## 配置

首次运行会在根目录生成 `config.ini`\
默认手台配置，大多数无需额外配置

托盘右键 ->  打开配置

```
; 更改之后托盘右键 -> 重新加载配置（或重启程序）

; 按住该键显示小窗
hotkey.hold=Alt
; 固定 / 取消固定(全局显示小窗)
hotkey.pin=Ctrl+Alt+O

; 小窗左上角坐标(手台推荐配置)
x=300
y=1600

; 小窗大小
width=400
height=280

; 放大倍数
zoom=1

; 开机自启：1 打开 / 0 关闭（默认关闭）
autostart=0

; 小窗显示系统原生鼠标光标：1 显示 / 0 用十字准星
cursor.real=1

; 以下需配置有效区后启用

; 启用有效区：1 启用 / 0 关闭
valid=1
; 是否在屏幕上画出有效区边缘：1 显示 / 0 不显示
valid.outline=0

; 矩形区域：左上x,左上y;右下x,右下y(无需矩形可在valid前加';'注释)
valid.rect=200,100;1062,389

; 圆形：圆周上任意三点x1,y1;x2,y2;x3,y3(无需圆形可在valid前加';'注释)
valid.circle=1062,1367;162,1758;504,1905

; 有全屏程序时不自动显示小窗：1 隐藏 / 0 照常显示
fullscreen.hide=1
```

更改之后托盘右键 ->「重新加载 config.ini」