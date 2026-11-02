Title: Notifications and the Notification Area - Win32 apps

URL Source: https://learn.microsoft.com/en-us/windows/win32/shell/notification-area

Markdown Content:
The notification area is a portion of the taskbar that provides a temporary source for notifications and status. It can also be used to display icons for system and program features that have no presence on the desktop, such as battery level, volume control, and network status. The notification area has been known historically as the system tray or status area.

This topic contains the following sections:

*   [Notification and Notification Area Guidelines](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#notification-and-notification-area-guidelines)
*   [Creating and Displaying a Notification](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#notifications-and-the-notification-area)
    *   [Add a Notification Icon](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#add-a-notification-icon)
    *   [Define the NOTIFYICONDATA Version](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#define-the-notifyicondata-version)
    *   [Define the Notification Look and Contents](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#define-the-notification-look-and-contents)
    *   [Check the User Status](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#check-the-user-status)
    *   [Display the Notification](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#display-the-notification)
    *   [Removing an Icon](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#removing-an-icon)

*   [SDK Sample](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#sdk-sample)
*   [Related topics](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#related-topics)

See the [Notifications](https://learn.microsoft.com/en-us/windows/win32/uxguide/mess-notif) and [Notification Area](https://learn.microsoft.com/en-us/windows/win32/uxguide/winenv-notification) sections of the [Windows User Experience Interaction Guidelines](https://learn.microsoft.com/en-us/windows/win32/uxguide/interaction) for best practices in the use of notifications and the notification area. The goal is to provide user benefit through appropriate use of notifications, without being annoying or distracting.

The notification area is not for critical information that must be acted on immediately. It is also not intended for quick program or command access. As of Windows 7, much of that functionality is best accomplished through an application's taskbar button.

Windows 7 allows a user to suppress all notifications from an application if they choose, so thoughtful notification design and use will incline the user to allow your application to continue to display them. Notifications are an interruption; ensure that they are worth it.

Windows 7 introduces the concept of "quiet time". Quiet time is defined as the first hour after a new user logs into his or her account either for the first time or for the first time after an operating system upgrade or clean installation. This time is set aside to allow the user to explore and familiarize themselves with the new environment without the distraction of notifications. During this time, most notifications should not be sent or shown. Exceptions include feedback that the user would expect to see in response to a user action, such as when he or she plugs in a USB device or prints a document. API specifics of regarding quiet time are discussed later in this topic.

The remaining sections in this topic outline the basic procedure to follow to display a notification from your application to the user.

1.   [Add a Notification Icon](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#add-a-notification-icon)
2.   [Define the NOTIFYICONDATA Version](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#define-the-notifyicondata-version)
3.   [Define the Notification Look and Contents](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#define-the-notification-look-and-contents)
4.   [Check the User Status](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#check-the-user-status)
5.   [Display the Notification](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#display-the-notification)
6.   [Removing an Icon](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area#removing-an-icon)

To display a notification, you must have an icon in the notification area. In certain cases, such as Microsoft Communicator or battery level, that icon will already be present. In many other cases, however, you will add an icon to the notification area only as long as is needed to show the notification. In either case, this is accomplished using the [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona) function. **Shell_NotifyIcon** allows you to add, modify, or delete an icon in the notification area.

![Image 1: notification area containing three icons](https://learn.microsoft.com/en-us/windows/win32/shell/images/taskbar/notificationareaicons.png)

When an icon is added to the notification area on Windows 7, it is added to the overflow section of the notification area by default. This area contains notification area icons that are active, but not visible in the notification area. Only the user can promote an icon from the overflow to the notification area, although in certain circumstances the system can temporarily promote an icon into the notification area as a short preview (under one minute).

Note

The user should have the final say on which icons they want to see in their notification area. Before installing a non-transient icon in the notification area, the user should be asked for permission. They should also be given the option (normally though its shortcut menu) to remove the icon from the notification area.

The [**NOTIFYICONDATA**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/ns-shellapi-notifyicondataa) structure sent in the call to [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona) contains information that specifies both the notification area icon and the notification itself. The following are those items specific to the notification area icon itself that can be set through **NOTIFYICONDATA**.

*   The resource from which the icon is taken.
*   A unique identifier for the icon.
*   The style of the icon's tooltip.
*   The icon's state (hidden, shared, or both) in the notification area.
*   The handle of an application window associated with the icon.
*   A callback message identifier that allows the icon to communicate events that occur within the icon's bounding rectangle and balloon notification with the associated application window. The icon's bounding rectangle can be retrieved through [**Shell_NotifyIconGetRect**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicongetrect).

Each icon in the notification area can be identified in two ways:

*   The GUID with which the icon is declared in the registry. This is the preferred method on Windows 7 and later.
*   The handle of a window associated with the notification area icon, plus an application-defined icon identifier. This method is used on Windows Vista and earlier.

Icons in the notification area can have a tooltip. The tooltip can be either a standard tooltip (preferred) or an application-drawn, pop-up UI. While a tooltip is not required, it is recommended.

Notification area icons should be high-DPI aware. An application should provide both a 16x16 pixel icon and a 32x32 icon in its resource file, and then use [**LoadIconMetric**](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-loadiconmetric) to ensure that the correct icon is loaded and scaled appropriately.

The application responsible for the notification area icon should handle a mouse click for that icon. When a user right-clicks the icon, it should bring up a normal shortcut menu. However, the result of a single click with the left mouse button will vary with the function of the icon. It should display what the user would expect to see in the form best suited to that content—a popup window, a dialog box or the program window itself. For instance, it could show status text for a status icon, or a slider for the volume control.

The placement of a popup window or dialog box that results from the click should be placed near the coordinate of the click in the notification area. Use the [**CalculatePopupWindowPosition**](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-calculatepopupwindowposition) to determine its location.

The icon can be added to the notification area without displaying a notification by defining only the icon-specific members of [**NOTIFYICONDATA**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/ns-shellapi-notifyicondataa) (discussed above) and calling [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona) as shown here:

```
NOTIFYICONDATA nid = {};
// Do NOT set the NIF_INFO flag.
...                    
Shell_NotifyIcon(NIM_ADD, &nid);
```

You can also add the icon to the notification area and display a notification all in one call to [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona). To do so, continue with the instructions in this topic.

As Windows has progressed, the [**NOTIFYICONDATA**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/ns-shellapi-notifyicondataa) structure has expanded to include more members to define more functionality. Constants are used to declare which version of **NOTIFYICONDATA** to use with your notification area icon, to allow for backward compatibility. Unless there is a compelling reason to do otherwise, it is strongly recommended that you use the NOTIFYICON_VERSION_4 version, introduced in Windows Vista. This version provides the full available functionality, including the preferred ability to identify the notification area icon though a registered GUID, a superior callback mechanism, and better accessibility.

Set the version through the following calls:

```
NOTIFYICONDATA nid = {};
... 
nid.uVersion = NOTIFYICON_VERSION_4;
// Add the icon
Shell_NotifyIcon(NIM_ADD, &nid);
// Set the version
Shell_NotifyIcon(NIM_SETVERSION, &nid);
```

Note that this call to [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona) does not display a notification.

A notification is a special type of balloon tooltip control. It contains a title, body text, and an icon. Like a window, it has a **Close** button in its upper right corner. It also contains an **Options** button that opens the Notification Area Icons item in the Control Panel, which allows the user to show or hide the icon or show only notifications without an icon.

![Image 2: screen shot of notification balloon indicating that battery power is low](https://learn.microsoft.com/en-us/windows/win32/shell/images/taskbar/notificationballoon.png)

The [**NOTIFYICONDATA**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/ns-shellapi-notifyicondataa) structure sent in the call to [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona) contains information that specifies both the notification area icon and the notification balloon itself. The following are those items specific to the notification that can be set through **NOTIFYICONDATA**.

*   An icon to display in the notification balloon, which is specified by the notification type. The size of the icon can be specified, as well as custom icons.
*   A notification title. This title should be a maximum of 48 characters long in English (to accommodate localization). The title is the first line of the notification, and set apart through the use of font size, color, and weight.
*   Text for use in the body of the notification. This text should be a maximum of 200 characters in English (to accommodate localization).
*   Whether the notification should be discarded if it cannot be displayed immediately.
*   A timeout for the notification. This setting is ignored in Windows Vista and later systems in favor of a system-wide accessibility timeout setting.
*   Whether the notification should respect quiet time, set through the [**NIIF_RESPECT_QUIET_TIME**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/ns-shellapi-notifyicondataa) flag.

Note

The [**IUserNotification**](https://learn.microsoft.com/en-us/windows/desktop/api/shobjidl_core/nn-shobjidl_core-iusernotification) and [**IUserNotification2**](https://learn.microsoft.com/en-us/windows/desktop/api/Shobjidl/nn-shobjidl-iusernotification2) interfaces are Component Object Model (COM) wrappers for [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona). However, at this time, they do not provide the full NOTIFYICON_VERSION_4 functionality available through **Shell_NotifyIcon** directly, including the use of a GUID to identify the notification area icon.

The system uses the [**SHQueryUserNotificationState**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shqueryusernotificationstate) function is used to check whether the user is in quiet time, away from the computer, or in an uninterruptable state such as Presentation mode. Whether the system displays your notification depends on this state.

Notifications sent when the user is away are queued for display, but because you cannot know when the user will return or whether the notification will still be valid at that time, you might consider resending the notification later.

Notifications sent during quiet time are discarded unshown. Design guidelines ask that all notifications be ignorable. They should not require immediate user action. Therefore, no notification is so important that it should override quiet time.

Once you have set the [**NOTIFYICONDATA**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/ns-shellapi-notifyicondataa) version and defined the notification in a **NOTIFYICONDATA** structure, call [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona) to display the icon.

*   If the notification area icon is not present, call [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona) to add the icon. Do this for both transient and non-transient icons.

```
NOTIFYICONDATA nid = {};
...                    
Shell_NotifyIcon(NIM_ADD, &nid);
```
*   If the notification area icon is already present, call [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona) to modify the icon.

```
NOTIFYICONDATA nid = {};
...                    
Shell_NotifyIcon(NIM_MODIFY, &nid);
```

The following code shows an example of setting [**NOTIFYICONDATA**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/ns-shellapi-notifyicondataa) data and sending it through [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona). Note that this example identifies the notification icon through a GUID (preferred in Windows 7).

```
// Declare NOTIFYICONDATA details. 
    // Error handling is omitted here for brevity. Do not omit it in your code.
    
    NOTIFYICONDATA nid = {};
    nid.cbSize = sizeof(nid);
    nid.hWnd = hWnd;
    nid.uFlags = NIF_ICON | NIF_TIP | NIF_GUID;
    
    // Note: This is an example GUID only and should not be used.
    // Normally, you should use a GUID-generating tool to provide the value to
    // assign to guidItem.
    static const GUID myGUID = 
    {0x23977b55, 0x10e0, 0x4041, {0xb8, 0x62, 0xb1, 0x95, 0x41, 0x96, 0x36, 0x69}};
    nid.guidItem = myGUID;
    
    // This text will be shown as the icon's tooltip.
    StringCchCopy(nid.szTip, ARRAYSIZE(nid.szTip), L"Test application");
    
    // Load the icon for high DPI.
    LoadIconMetric(hInst, MAKEINTRESOURCE(IDI_SMALL), LIM_SMALL, &(nid.hIcon));
    
    // Show the notification.
    Shell_NotifyIcon(NIM_ADD, &nid) ? S_OK : E_FAIL;
```

To remove an icon—for instance, when you have only added the icon temporarily to broadcast a notification—call [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona)as shown here. Only a minimal [**NOTIFYICONDATA**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/ns-shellapi-notifyicondataa) structure that identifies the icon is needed in this call.

```
NOTIFYICONDATA nid = {};
...                    
Shell_NotifyIcon(NIM_DELETE, &nid);
```

Note

When an application is uninstalled, its notification area icon can still appear to the user as an option in the Notification Area Icons page in the Control Panel for up to seven days. However, any changes made there will have no effect.

See the [NotificationIcon Sample](https://learn.microsoft.com/en-us/windows/win32/shell/samples-notificationicon) sample in the Windows Software Development Kit (SDK) for a full example of the use of [**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona).

[**Shell_NotifyIcon**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicona)

[**Shell_NotifyIconGetRect**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shell_notifyicongetrect)

[**NOTIFYICONDATA**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/ns-shellapi-notifyicondataa)

[**SHQueryUserNotificationState**](https://learn.microsoft.com/en-us/windows/desktop/api/Shellapi/nf-shellapi-shqueryusernotificationstate)

[**IUserNotification**](https://learn.microsoft.com/en-us/windows/desktop/api/shobjidl_core/nn-shobjidl_core-iusernotification)

[**IUserNotification2**](https://learn.microsoft.com/en-us/windows/desktop/api/Shobjidl/nn-shobjidl-iusernotification2)

[The Taskbar](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar)

[Taskbar Extensions](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar-extensions)
