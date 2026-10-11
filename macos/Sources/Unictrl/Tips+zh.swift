import Foundation

/// 提示清单的简体中文显示文案：按 id 查找，{pc} 会替换成 PC 名。
/// Tip 本体保持英文——它随协议（quality 消息）发给 PC，翻译只发生在 Mac 的显示层。
enum TipsZH {
    struct Item { let title: String; let summary: String; let steps: [String] }

    static let items: [String: Item] = [
        "bolt": Item(
            title: "用 Logi Bolt 接收器连接鼠标",
            summary: "蓝牙会与 Wi‑Fi、AirDrop 和通用控制共享这台 Mac 的无线电，指针因此时不时停顿。Bolt 接收器有自己的无线电。",
            steps: ["把 Logi Bolt 接收器插到这台 Mac 上（最好加一根短的 USB 延长线）。",
                    "在 Logi Options+ 里把鼠标与接收器配对一次；之后不需要任何罗技软件。",
                    "把那个 Easy‑Switch 通道留给这台 Mac。Unictrl 会自动通过接收器找到鼠标。"]),
        "receiver-placement": Item(
            title: "让接收器靠近鼠标",
            summary: "接收器的 2.4 GHz 信号很容易被笔记本机身挡住，也会被 USB 3 设备的噪声淹没。",
            steps: ["使用离鼠标最近的那个 USB 口。",
                    "更好的做法：一根短的 USB 延长线，让接收器放在桌面上、离鼠标 20–30 厘米、中间无遮挡。",
                    "让它远离 USB 3 集线器、扩展坞、移动硬盘和 Wi‑Fi 路由器：USB 3 发出的噪声正好落在 2.4 GHz 频段。"]),
        "wifi-band": Item(
            title: "使用 5 GHz 或 6 GHz Wi‑Fi",
            summary: "2.4 GHz 更慢也更拥挤，还与蓝牙和鼠标接收器共享空气。",
            steps: ["在这台 Mac 和 {pc} 上都连接路由器的 5 GHz（或 6 GHz）网络。",
                    "如果路由器所有频段共用一个名称，给 2.4 GHz 单独设一个名称，或开启频段引导（band steering）。",
                    "在两台电脑上忘记 2.4 GHz 网络，免得它们又悄悄连回去。"]),
        "router-channel": Item(
            title: "把路由器调到信道 149（或 36–48）",
            summary: "AirDrop 和通用控制使用信道 149（部分地区为 44）通信。同信道时这台 Mac 无需避开你的 Wi‑Fi，而且 52–144 之外的信道从不会被雷达强制清空。",
            steps: ["打开路由器的 App 或管理页面，进入 Wi‑Fi 设置的 5 GHz 频段。",
                    "把信道从“自动”改为 149；如果没有 149，就在 36–48 里选一个。",
                    "带宽保持 80 MHz：160 MHz 需要用到雷达信道。",
                    "两台电脑会在几秒内自动重连。"]),
        "wifi-signal": Item(
            title: "保持较强的 Wi‑Fi 信号",
            summary: "信号弱意味着重传，而每一次重传都是一次延迟尖峰。",
            steps: ["争取 −65 dBm 或更好。按住 Option 点菜单栏的 Wi‑Fi 图标可以看到数值。",
                    "把路由器或 mesh 节点放在同一个房间、开阔处：不要挡在屏幕后或柜子里。",
                    "用 mesh Wi‑Fi 时，确保两台电脑都连的是这个房间的节点。"]),
        "pc-signal": Item(
            title: "让 {pc} 的 Wi‑Fi 信号保持较强",
            summary: "笔记本的 Wi‑Fi 天线常常比 Mac 弱；链路弱的一端可能在 PC 这边。",
            steps: ["挪动 PC 或路由器，让它们在同一房间且之间无遮挡。",
                    "把 PC 连到与这台 Mac 相同的 5 GHz 网络和 mesh 节点。"]),
        "wired": Item(
            title: "能用网线就用网线",
            summary: "两台电脑都在 Wi‑Fi 上时，每一次移动都要两次穿过空气。任何一端插上网线，就能省掉其中一次。",
            steps: ["把 PC 插到路由器或交换机上；没有网口可以用 USB‑C 网卡。",
                    "这台 Mac 也可以插网线。两台电脑会自动改用有线；Wi‑Fi 可以保持开启。",
                    "实在没法插线？让两台电脑都待在同一个 5 GHz 网络和同一个 mesh 节点上。"]),
        "pc-power": Item(
            title: "把 {pc} 设为最佳性能",
            summary: "为了省电，Windows 会让 Wi‑Fi 网卡和处理器在数据包之间打盹，移动事件因此成批到达。",
            steps: ["设置 › 系统 › 电源和电池 › 电源模式：最佳性能（插电时）。",
                    "让 PC 保持插电：省电模式会拖慢网络和输入处理。",
                    "设备管理器 › 网络适配器 › Wi‑Fi 网卡 › 电源管理：取消勾选“允许计算机关闭此设备以节约电源”。",
                    "同一网卡 › 高级：把 MIMO Power Save Mode 设为“No SMPS”，如有 Roaming Aggressiveness 则设为 Lowest。"]),
        "vpn": Item(
            title: "让本地流量绕开 VPN",
            summary: "承载本地流量的 VPN 会把每一次移动都送进隧道，徒增延迟和尖峰。",
            steps: ["在 VPN 应用里允许本地网络访问（通常叫“允许 LAN”或“本地网络共享”）。",
                    "或者在使用 Unictrl 时暂停 VPN。"]),
        "mac-low-power": Item(
            title: "关闭这台 Mac 的低电量模式",
            summary: "低电量模式会降低处理器速度并让 Wi‑Fi 打盹，从而拖慢移动事件。",
            steps: ["系统设置 › 电池 › 低电量模式：选“从不”，或选“只在电池供电时”并保持插电。"]),
        "mac-busy": Item(
            title: "保持这台 Mac 的响应速度",
            summary: "Mac 忙碌时，鼠标事件会晚到 Unictrl。",
            steps: ["打开活动监视器 › CPU，按 % CPU 排序。",
                    "退出或暂停占用高的程序：视频导出、索引、失控的浏览器标签页。",
                    "如果不需要，退出其他监听键盘鼠标的工具（其他 KVM 或宏工具）：每一个都会处理所有事件。"]),
        "pc-busy": Item(
            title: "保持 {pc} 的响应速度",
            summary: "PC 忙碌时，移动事件要很久才能生效。",
            steps: ["打开任务管理器 › 进程，按 CPU 排序。",
                    "关闭或暂停占用高的程序，比如更新、扫描或后台游戏。"]),
        "pointer-feel": Item(
            title: "如果指针在 PC 上觉得发飘",
            summary: "快速移动后指针还在滑行，是速度设置的问题，不是延迟。",
            steps: ["Unictrl 设置 › PC 上的指针速度：试试 90–95%。",
                    "在 PC 上，设置 › 蓝牙和其他设备 › 鼠标 › 其他鼠标设置 › 指针选项：勾选“提高指针精确度”可得类似 Mac 的加速，不勾则手感更平稳。两种都试试。"]),
    ]
}

extension Tip {
    /// 系统语言为中文时的显示版本；其他语言原样返回。
    func localized(pc: String?) -> Tip {
        guard Locale.preferredLanguages.first?.hasPrefix("zh") == true,
              let zh = TipsZH.items[id] else { return self }
        func expand(_ s: String) -> String {
            guard let pc, s.contains("{pc}") else { return s }
            return s.replacingOccurrences(of: "{pc}", with: pc)
        }
        return Tip(id: id, status: status, title: expand(zh.title), summary: expand(zh.summary),
                   steps: zh.steps.map(expand), seen: seen)
    }
}
