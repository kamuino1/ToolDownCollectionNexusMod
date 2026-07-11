AutoItSetOption("WinTitleMatchMode", 2)

While 1
    If WinExists("Download File Info") Then
        WinActivate("Download File Info")
        WinWaitActive("Download File Info", "", 5)
        Sleep(1000)
        Send("{ENTER}") ; Start Download
        Sleep(2000)
        Send("^p") ; Pause ngay sau khi IDM bắt đầu tải
        ExitLoop
    EndIf
    Sleep(500)
WEnd
