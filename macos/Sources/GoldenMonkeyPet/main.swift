import AppKit
import GoldenMonkeyShared

private enum PetState: Equatable { case idle, walking, turning, expression, dragging, bouncing, sleeping, waking }

private final class FloatingEffect {
    enum Kind { case text(String), heart(NSColor, CGFloat) }
    let kind: Kind
    var point: CGPoint
    var age = 0
    let lifetime: Int
    let rise: CGFloat
    let drift: CGFloat
    init(kind: Kind, point: CGPoint, lifetime: Int, rise: CGFloat, drift: CGFloat = 0) {
        self.kind = kind; self.point = point; self.lifetime = lifetime; self.rise = rise; self.drift = drift
    }
}

private final class PetView: NSView {
    private let random = SystemRandomNumberGenerator()
    private var frames: [String: NSImage] = [:]
    private var state: PetState = .idle
    private var sleeping = false, walkingPaused = false, dragPending = false, draggingPet = false
    private var headPress = false, doubleClickHandled = false, pendingPat = false
    private var pendingPatAt = Date.distantFuture, sleepStarted = Date(), nextSleepText = Date.distantFuture
    private var nextIdleAction = Date().addingTimeInterval(7), nextBlink = Date(), nextWalk = Date()
    private var nextFrame = Date(), animation: [String] = [], animationIndex = 0, animationInterval = 0.1
    private var walkStepsRemaining = 0, walkDx = 0, walkBob = 0, facingDx = 0, pendingWalkDx = 0
    private var turnTicksRemaining = 0, turnFrame = 0, bounceIndex = 0, petYOffset: CGFloat = 0
    private var dragStartScreen = CGPoint.zero, dragStartWindow = CGPoint.zero
    private var effects: [FloatingEffect] = []
    private var timers: [Timer] = []
    private var codex = CodexSnapshot(), statusOpened = Date(), statusHiddenUntil = Date.distantPast, pulse = 0
    private let bounceOffsets: [CGFloat] = [0, -8, -12, -8, -3, 2, 0]
    private var walkMenuItem: NSMenuItem!, sleepMenuItem: NSMenuItem!, topMenuItem: NSMenuItem!
    private var rng = SystemRandomNumberGenerator()

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    init() throws {
        super.init(frame: NSRect(x: 0, y: 0, width: 167, height: 222))
        wantsLayer = true
        layer?.backgroundColor = NSColor.clear.cgColor
        try loadFrames()
        buildMenu()
        nextBlink = Date().addingTimeInterval(Double.random(in: 2.2...4.8, using: &rng))
        nextWalk = Date().addingTimeInterval(Double.random(in: 1.4...2.6, using: &rng))
        startTimers()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    private func resource(_ relative: String) -> URL {
        Bundle.main.resourceURL!.appendingPathComponent(relative)
    }

    private func loadFrames() throws {
        let base = resource("assets/sprites/girlfriend_cherry_v2")
        for name in ["idle", "blink_half", "blink_closed", "happy", "walk_clean_1", "walk_clean_2", "walk_clean_3", "walk_clean_4"] {
            let url = base.appendingPathComponent(name + ".png")
            guard let image = NSImage(contentsOf: url) else { throw NSError(domain: "GoldenMonkeyPet", code: 1, userInfo: [NSLocalizedDescriptionKey: "缺少动画素材：\(url.path)"]) }
            frames[name] = image
        }
        try loadActionFrames(resource("assets/sprites/sleep_seated_sheet.png"), names: ["sleep", "wake"], heightRatio: 0.80)
        try loadActionFrames(resource("assets/sprites/idle_actions_sheet.png"), names: ["tilt", "cherry", "groom"], heightRatio: 0.97)
        frames["sleep_breathe"] = shifted(frames["sleep"]!, y: -1, extraHeight: 1)
        createDirectionalFrames()
    }

    private func loadActionFrames(_ url: URL, names: [String], heightRatio: CGFloat) throws {
        guard let sheet = NSImage(contentsOf: url), let bitmap = bitmapRep(sheet) else { throw NSError(domain: "GoldenMonkeyPet", code: 2) }
        let cellWidth = bitmap.pixelsWide / names.count
        var bounds: [NSRect] = []
        var maxWidth = 1, maxHeight = 1
        for cell in 0..<names.count {
            var left = (cell + 1) * cellWidth, right = -1, top = bitmap.pixelsHigh, bottom = -1
            for y in 0..<bitmap.pixelsHigh {
                for x in (cell * cellWidth)..<((cell + 1) * cellWidth) {
                    if (bitmap.colorAt(x: x, y: y)?.alphaComponent ?? 0) >= 0.5 {
                        left = min(left, x); right = max(right, x); top = min(top, y); bottom = max(bottom, y)
                    }
                }
            }
            guard right >= left else { throw NSError(domain: "GoldenMonkeyPet", code: 3) }
            let rect = NSRect(x: left, y: top, width: right - left + 1, height: bottom - top + 1)
            bounds.append(rect); maxWidth = max(maxWidth, Int(rect.width)); maxHeight = max(maxHeight, Int(rect.height))
        }
        let idleSize = frames["idle"]!.size
        let scale = min((idleSize.width - 12) / CGFloat(maxWidth), idleSize.height * heightRatio / CGFloat(maxHeight))
        for (index, name) in names.enumerated() {
            let source = bounds[index]
            let width = floor(source.width * scale), height = floor(source.height * scale)
            frames[name] = render(size: idleSize) {
                sheet.draw(in: NSRect(x: (idleSize.width - width) / 2, y: idleSize.height - 3 - height, width: width, height: height),
                           from: source, operation: .copy, fraction: 1, respectFlipped: true, hints: [.interpolation: NSImageInterpolation.none])
            }
        }
    }

    private func bitmapRep(_ image: NSImage) -> NSBitmapImageRep? {
        guard let data = image.tiffRepresentation else { return nil }
        return NSBitmapImageRep(data: data)
    }

    private func render(size: NSSize, drawing: () -> Void) -> NSImage {
        let image = NSImage(size: size)
        image.lockFocusFlipped(true); NSGraphicsContext.current?.imageInterpolation = .none
        drawing(); image.unlockFocus(); return image
    }

    private func mirrored(_ source: NSImage) -> NSImage {
        render(size: source.size) {
            NSGraphicsContext.current?.saveGraphicsState()
            let transform = NSAffineTransform(); transform.translateX(by: source.size.width, yBy: 0); transform.scaleX(by: -1, yBy: 1); transform.concat()
            source.draw(in: NSRect(origin: .zero, size: source.size), from: .zero, operation: .copy, fraction: 1, respectFlipped: true, hints: [.interpolation: NSImageInterpolation.none])
            NSGraphicsContext.current?.restoreGraphicsState()
        }
    }

    private func squashed(_ source: NSImage, scaleX: CGFloat) -> NSImage {
        render(size: source.size) {
            let width = max(1, floor(source.size.width * scaleX))
            source.draw(in: NSRect(x: (source.size.width - width) / 2, y: 0, width: width, height: source.size.height), from: .zero, operation: .copy, fraction: 1, respectFlipped: true, hints: [.interpolation: NSImageInterpolation.none])
        }
    }

    private func shifted(_ source: NSImage, y: CGFloat, extraHeight: CGFloat) -> NSImage {
        render(size: source.size) {
            source.draw(in: NSRect(x: 0, y: y, width: source.size.width, height: source.size.height + extraHeight), from: .zero, operation: .copy, fraction: 1, respectFlipped: true, hints: [.interpolation: NSImageInterpolation.none])
        }
    }

    private func createDirectionalFrames() {
        let idle = frames["idle"]!, idleLeft = mirrored(idle); frames["idle_left"] = idleLeft
        for name in ["blink_half", "blink_closed", "happy", "sleep", "sleep_breathe", "wake", "tilt", "cherry", "groom"] { frames[name + "_left"] = mirrored(frames[name]!) }
        for (index, scale) in [0.86, 0.68, 0.86].enumerated() {
            frames["turn_right_\(index + 1)"] = squashed(idle, scaleX: scale)
            frames["turn_left_\(index + 1)"] = squashed(idleLeft, scaleX: scale)
        }
        for i in 1...4 {
            frames["walk_right_\(i)"] = frames["walk_clean_\(i)"]
            frames["walk_left_\(i)"] = mirrored(frames["walk_clean_\(i)"]!)
        }
    }

    private func startTimers() {
        schedule(0.045) { [weak self] in self?.effectTick() }
        schedule(0.070) { [weak self] in self?.animationTick() }
        schedule(0.030) { [weak self] in self?.movementTick() }
        schedule(0.032) { [weak self] in self?.bounceTick() }
        schedule(0.700) { [weak self] in self?.codexTick() }
        schedule(6.500) { [weak self] in guard let self, !self.sleeping, !self.dragPending, Double.random(in: 0...1, using: &self.rng) < 0.35 else { return }; self.addHearts(1) }
    }

    private func schedule(_ interval: TimeInterval, action: @escaping () -> Void) {
        let timer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { _ in action() }
        RunLoop.main.add(timer, forMode: .common); timers.append(timer)
    }

    private func buildMenu() {
        let menu = NSMenu()
        menu.addItem(item("喂樱桃", #selector(feed)))
        menu.addItem(item("爱心留言", #selector(loveNote)))
        menu.addItem(item("摸摸头", #selector(patHead)))
        walkMenuItem = item("停止走动", #selector(toggleWalking)); menu.addItem(walkMenuItem)
        sleepMenuItem = item("睡觉", #selector(toggleSleep)); menu.addItem(sleepMenuItem)
        menu.addItem(.separator())
        topMenuItem = item("始终置顶", #selector(toggleTop)); topMenuItem.state = .on; menu.addItem(topMenuItem)
        menu.addItem(item("安装 Codex 联动", #selector(installCodexHooks)))
        menu.addItem(.separator())
        menu.addItem(item("退出", #selector(quit)))
        self.menu = menu
    }

    private func item(_ title: String, _ action: Selector) -> NSMenuItem {
        let value = NSMenuItem(title: title, action: action, keyEquivalent: ""); value.target = self; return value
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.clear.setFill(); dirtyRect.fill()
        let pose = currentFrame()
        pose.draw(in: NSRect(x: 0, y: 28 + petYOffset, width: pose.size.width, height: pose.size.height), from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: [.interpolation: NSImageInterpolation.none])
        drawEffects()
        drawStatus()
    }

    private func currentFrame() -> NSImage { frames[currentFrameName()] ?? frames["idle"]! }
    private var displayedFrame = "idle"
    private func currentFrameName() -> String { displayedFrame }
    private func facing(_ name: String) -> String { facingDx < 0 ? name + "_left" : name }
    private func setFrame(_ name: String) { displayedFrame = frames[name] == nil ? "idle" : name; needsDisplay = true }

    private func drawEffects() {
        for effect in effects {
            switch effect.kind {
            case .text(let text):
                text.draw(at: effect.point, withAttributes: [.font: NSFont.boldSystemFont(ofSize: 16), .foregroundColor: NSColor(calibratedRed: 0.42, green: 0.23, blue: 0.09, alpha: 1)])
            case .heart(let color, let size):
                color.setFill(); heartPath(at: effect.point, size: size).fill()
            }
        }
    }

    private func heartPath(at p: CGPoint, size: CGFloat) -> NSBezierPath {
        let path = NSBezierPath(); path.move(to: CGPoint(x: p.x + size * 0.5, y: p.y + size * 0.92))
        path.curve(to: CGPoint(x: p.x + size * 0.06, y: p.y + size * 0.35), controlPoint1: CGPoint(x: p.x + size * 0.15, y: p.y + size * 0.68), controlPoint2: CGPoint(x: p.x, y: p.y + size * 0.55))
        path.curve(to: CGPoint(x: p.x + size * 0.5, y: p.y + size * 0.18), controlPoint1: CGPoint(x: p.x + size * 0.03, y: p.y + size * 0.08), controlPoint2: CGPoint(x: p.x + size * 0.34, y: p.y + size * 0.05))
        path.curve(to: CGPoint(x: p.x + size * 0.94, y: p.y + size * 0.35), controlPoint1: CGPoint(x: p.x + size * 0.66, y: p.y + size * 0.05), controlPoint2: CGPoint(x: p.x + size * 0.97, y: p.y + size * 0.08))
        path.curve(to: CGPoint(x: p.x + size * 0.5, y: p.y + size * 0.92), controlPoint1: CGPoint(x: p.x + size, y: p.y + size * 0.55), controlPoint2: CGPoint(x: p.x + size * 0.85, y: p.y + size * 0.68))
        path.close(); return path
    }

    private func drawStatus() {
        let now = Date()
        let recentEnd = ["ended", "interrupted"].contains(codex.phase) && now.timeIntervalSince(codex.revision) < 6
        let show = codex.active > 0 || ["unknown", "disconnected"].contains(codex.phase) || recentEnd
        guard show, now >= statusHiddenUntil else { return }
        var caption = codex.phase == "waiting" ? "等你确认" : codex.phase == "busy" ? (codex.text.contains("修改") ? "改代码中" : codex.text.contains("工具") ? "执行中" : "忙碌中") : codex.phase == "ended" ? "本轮结束" : codex.phase == "interrupted" ? "已暂停" : codex.phase == "unknown" ? "状态待更新" : "待连接"
        if codex.active > 1 { caption += " · \(codex.active)" }
        let compact = codex.phase == "disconnected" && now.timeIntervalSince(statusOpened) > 6
        let textWidth = (caption as NSString).size(withAttributes: [.font: NSFont.systemFont(ofSize: 12)]).width
        let width = compact ? 20 : min(bounds.width - 8, textWidth + 36)
        let rect = NSRect(x: (bounds.width - width) / 2, y: 0, width: width, height: 22)
        let accent = codex.phase == "waiting" ? NSColor(calibratedRed: 0.73, green: 0.42, blue: 0.16, alpha: 1) : codex.phase == "ended" ? NSColor(calibratedRed: 0.30, green: 0.49, blue: 0.40, alpha: 1) : NSColor(calibratedRed: 0.51, green: 0.40, blue: 0.27, alpha: 1)
        (codex.phase == "waiting" ? NSColor(calibratedRed: 1, green: 0.92, blue: 0.78, alpha: 0.98) : NSColor(calibratedRed: 1, green: 0.98, blue: 0.93, alpha: 0.98)).setFill()
        let bubble = NSBezierPath(roundedRect: rect, xRadius: 7, yRadius: 7); bubble.fill(); NSColor(calibratedRed: 0.80, green: 0.72, blue: 0.56, alpha: 1).setStroke(); bubble.lineWidth = 1; bubble.stroke()
        accent.setFill()
        if codex.phase == "busy" {
            for i in 0..<3 { NSBezierPath(ovalIn: NSRect(x: rect.minX + 8 + CGFloat(i * 5), y: 10 - (pulse % 3 == i ? 2 : 0), width: 3, height: 3)).fill() }
        } else {
            let symbol = codex.phase == "waiting" ? "!" : codex.phase == "ended" ? "✓" : codex.phase == "interrupted" ? "Ⅱ" : "○"
            symbol.draw(in: NSRect(x: rect.minX + 4, y: 3, width: compact ? 12 : 20, height: 17), withAttributes: [.font: NSFont.systemFont(ofSize: 12), .foregroundColor: accent, .paragraphStyle: centeredStyle()])
        }
        if !compact { caption.draw(in: NSRect(x: rect.minX + 27, y: 3, width: rect.width - 31, height: 17), withAttributes: [.font: NSFont.systemFont(ofSize: 12), .foregroundColor: accent]) }
        toolTip = codex.text
    }

    private func centeredStyle() -> NSParagraphStyle { let p = NSMutableParagraphStyle(); p.alignment = .center; return p }

    private func enter(_ next: PetState) {
        pendingPat = false; walkStepsRemaining = 0; turnTicksRemaining = 0; walkBob = 0; animation.removeAll(); animationIndex = 0; bounceIndex = bounceOffsets.count; petYOffset = 0; state = next
        setFrame(facing(sleeping ? "sleep" : "idle")); nextWalk = Date().addingTimeInterval(Double.random(in: 1.6...3.2, using: &rng)); nextBlink = Date().addingTimeInterval(Double.random(in: 2.4...5.2, using: &rng))
    }

    @objc private func toggleSleep() { sleeping ? wakeUp() : goToSleep() }
    private func goToSleep() { sleeping = true; sleepMenuItem.title = "叫醒"; enter(.sleeping); sleepStarted = Date(); nextSleepText = Date().addingTimeInterval(5); effects.removeAll(); addText("Zzz...") }
    private func wakeUp() { guard sleeping else { return }; sleeping = false; sleepMenuItem.title = "睡觉"; effects.removeAll(); startAnimation(["wake", "wake", "idle"], interval: 0.22); state = .waking; setFrame(facing("wake")) }
    @objc private func toggleWalking() { walkingPaused.toggle(); walkMenuItem.title = walkingPaused ? "恢复走动" : "停止走动"; if state == .walking || state == .turning { enter(.idle) }; nextWalk = Date().addingTimeInterval(Double.random(in: 1.4...2.6, using: &rng)) }
    @objc private func toggleTop() { guard let window else { return }; let on = topMenuItem.state != .on; topMenuItem.state = on ? .on : .off; window.level = on ? .floating : .normal }
    @objc private func quit() { NSApp.terminate(nil) }

    private func startAnimation(_ names: [String], interval: TimeInterval) { enter(.expression); nextWalk = Date().addingTimeInterval(Double(names.count) * interval + 1.8); animation = names; animationIndex = 0; animationInterval = interval; nextFrame = Date() }
    private func happy() { let waking = state == .waking; startAnimation(waking ? ["wake", "wake", "happy", "happy", "idle"] : ["happy", "happy", "happy", "idle"], interval: 0.32); if waking { setFrame(facing("wake")) } }
    @objc private func patHead() { guard !sleeping, !draggingPet, state != .waking else { return }; startAnimation(["blink_half", "tilt", "tilt", "happy", "idle"], interval: 0.20); addHearts(2); nextIdleAction = Date().addingTimeInterval(Double.random(in: 8...15, using: &rng)) }
    private func idleAction() { guard !sleeping, !dragPending, state == .idle else { return }; let pose = ["tilt", "cherry", "groom"].randomElement(using: &rng)!; startAnimation(["idle", pose, pose, pose, "idle"], interval: 0.26); nextIdleAction = Date().addingTimeInterval(Double.random(in: 8...15, using: &rng)) }
    @objc private func feed() { wakeUp(); addText("这颗樱桃送给你"); addHearts(5); happy() }
    @objc private func loveNote() { wakeUp(); let notes = ["好想你呀", "樱桃送给你", "今天也要开心", "我会一直陪着你", "记得喝水呀", "偷偷亲你一下", "小猴子在陪你", "不开心就摸摸我", "今天也辛苦啦", "把好运分你一半", "想和你一起吃樱桃", "你一来我就开心"]; addText(notes.randomElement(using: &rng)!); addHearts(7); happy() }

    private func addText(_ text: String) { statusHiddenUntil = Date().addingTimeInterval(2.7); effects.append(FloatingEffect(kind: .text(text), point: CGPoint(x: 4, y: 3), lifetime: 58, rise: 1)); needsDisplay = true }
    private func addHearts(_ count: Int) { let colors = [NSColor(calibratedRed: 1, green: 0.42, blue: 0.54, alpha: 1), NSColor(calibratedRed: 1, green: 0.56, blue: 0.69, alpha: 1), NSColor(calibratedRed: 0.91, green: 0.29, blue: 0.42, alpha: 1)]; for _ in 0..<count { let size = CGFloat(Int.random(in: 16...24, using: &rng)); effects.append(FloatingEffect(kind: .heart(colors.randomElement(using: &rng)!, size), point: CGPoint(x: CGFloat.random(in: bounds.width / 3...bounds.width * 2 / 3, using: &rng), y: CGFloat.random(in: bounds.height / 3...bounds.height / 2, using: &rng)), lifetime: 42, rise: 2, drift: CGFloat(Int.random(in: -1...1, using: &rng)))) }; needsDisplay = true }

    private func effectTick() { for effect in effects { effect.age += 1; effect.point.x += effect.drift; effect.point.y -= effect.rise }; effects.removeAll { $0.age > $0.lifetime }; needsDisplay = true }
    private func animationTick() {
        let now = Date()
        if pendingPat, now >= pendingPatAt, !dragPending { pendingPat = false; patHead() }
        if sleeping { guard !dragPending else { return }; let phase = now.timeIntervalSince(sleepStarted).truncatingRemainder(dividingBy: 3.2); setFrame(facing(phase >= 1 && phase < 2.2 ? "sleep_breathe" : "sleep")); if now >= nextSleepText { addText("Zzz..."); nextSleepText = now.addingTimeInterval(Double.random(in: 5...9, using: &rng)) }; return }
        guard !dragPending, state == .idle || state == .expression || state == .waking else { return }
        if animationIndex < animation.count { guard now >= nextFrame else { return }; setFrame(facing(animation[animationIndex])); animationIndex += 1; nextFrame = now.addingTimeInterval(animationInterval) }
        else if (state == .expression || state == .waking), now >= nextFrame { enter(.idle) }
        else if state == .idle, codex.active > 0 { setFrame(facing(codex.phase == "waiting" ? "tilt" : "cherry")) }
        else if state == .idle, now >= nextIdleAction { idleAction() }
        else if now >= nextBlink { startAnimation(["blink_half", "blink_closed", "blink_half", "idle"], interval: 0.09) }
    }

    private func movementTick() {
        guard codex.active == 0, !sleeping, !dragPending, !pendingPat, state == .idle || state == .walking || state == .turning else { return }
        if walkingPaused { if state == .walking || state == .turning { enter(.idle) }; setFrame(facing("idle")); return }
        guard let window, let screen = window.screen ?? NSScreen.main else { return }
        let area = screen.visibleFrame, now = Date()
        if walkStepsRemaining <= 0 {
            if turnTicksRemaining > 0 { setFrame("turn_\(pendingWalkDx < 0 ? "left" : "right")_\(min(2, turnFrame) + 1)"); turnFrame += 1; turnTicksRemaining -= 1; if turnTicksRemaining == 0 { walkDx = pendingWalkDx; facingDx = walkDx; enter(.walking); walkStepsRemaining = Int.random(in: 3...6, using: &rng) * 16 }; return }
            guard now >= nextWalk else { return }
            pendingWalkDx = Bool.random(using: &rng) ? -1 : 1
            if window.frame.minX <= area.minX + 20 { pendingWalkDx = 1 }; if window.frame.maxX >= area.maxX - 20 { pendingWalkDx = -1 }
            if facingDx != 0, pendingWalkDx != facingDx { enter(.turning); turnTicksRemaining = 3; turnFrame = 0; return }
            walkDx = pendingWalkDx; facingDx = walkDx; enter(.walking); walkStepsRemaining = Int.random(in: 3...6, using: &rng) * 16
        }
        var origin = window.frame.origin; origin.x = max(area.minX, min(area.maxX - window.frame.width, origin.x + CGFloat(walkDx))); window.setFrameOrigin(origin)
        setFrame("walk_\(walkDx < 0 ? "left" : "right")_\((walkBob / 4) % 4 + 1)"); walkBob += 1; walkStepsRemaining -= 1
        if walkStepsRemaining <= 0 { enter(.idle); nextWalk = Date().addingTimeInterval(Double.random(in: 2.6...6.2, using: &rng)); if Double.random(in: 0...1, using: &rng) < 0.25 { addText("我走到你旁边啦") } }
    }

    private func bounceTick() { guard state == .bouncing, !dragPending, !draggingPet else { return }; if bounceIndex >= bounceOffsets.count { enter(.idle); nextWalk = Date().addingTimeInterval(Double.random(in: 1.2...2.6, using: &rng)); return }; petYOffset = bounceOffsets[bounceIndex]; bounceIndex += 1; needsDisplay = true }
    private func codexTick() { let old = codex.revision; codex = CodexStatus.read(); pulse += 1; needsDisplay = true; if sleeping || dragPending || draggingPet { return }; if codex.active > 0, state == .walking || state == .turning { enter(.idle) }; if codex.revision != old, old != .distantPast, codex.active == 0, codex.phase == "ended", Date().timeIntervalSince(codex.revision) < 15, state == .idle { happy() } }

    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 {
            pendingPat = false; doubleClickHandled = true
            if sleeping { wakeUp(); addText("我醒啦") } else { loveNote() }
            return
        }
        dragPending = true; draggingPet = false; dragStartScreen = NSEvent.mouseLocation; dragStartWindow = window?.frame.origin ?? .zero; doubleClickHandled = false
        let local = convert(event.locationInWindow, from: nil); headPress = local.x >= bounds.width / 8 && local.x < bounds.width * 7 / 8 && local.y >= 28 && local.y < 28 + 120
        if !sleeping { enter(.idle) }
    }
    override func mouseDragged(with event: NSEvent) { guard dragPending, let window else { return }; let current = NSEvent.mouseLocation, dx = current.x - dragStartScreen.x, dy = current.y - dragStartScreen.y; guard abs(dx) + abs(dy) >= 5 else { return }; if !draggingPet { enter(.dragging) }; draggingPet = true; window.setFrameOrigin(CGPoint(x: dragStartWindow.x + dx, y: dragStartWindow.y + dy)) }
    override func mouseUp(with event: NSEvent) { let bounced = draggingPet; dragPending = false; draggingPet = false; if sleeping { enter(.sleeping) } else if bounced { enter(.bouncing); bounceIndex = 0 } else if headPress, !doubleClickHandled, state != .waking { pendingPat = true; pendingPatAt = Date().addingTimeInterval(NSEvent.doubleClickInterval) }; headPress = false }
    override func rightMouseDown(with event: NSEvent) { if let menu { NSMenu.popUpContextMenu(menu, with: event, for: self) } }

    @objc private func installCodexHooks() {
        do {
            let hook = Bundle.main.bundleURL.appendingPathComponent("Contents/MacOS/GoldenMonkeyCodexHook").path
            guard FileManager.default.isExecutableFile(atPath: hook) else { throw NSError(domain: "GoldenMonkeyPet", code: 9, userInfo: [NSLocalizedDescriptionKey: "应用包内缺少 Codex 桥接程序"]) }
            let config = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".codex/hooks.json")
            try FileManager.default.createDirectory(at: config.deletingLastPathComponent(), withIntermediateDirectories: true)
            var root: [String: Any] = [:]
            if let data = try? Data(contentsOf: config), let existing = try? JSONSerialization.jsonObject(with: data) as? [String: Any] { root = existing }
            var hooks = root["hooks"] as? [String: Any] ?? [:]
            let command = "'" + hook.replacingOccurrences(of: "'", with: "'\\''") + "'"
            for event in ["UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest", "Stop", "Interrupt", "SessionEnd"] {
                var entries = hooks[event] as? [[String: Any]] ?? []
                let already = entries.contains { entry in ((entry["hooks"] as? [[String: Any]]) ?? []).contains { ($0["command"] as? String ?? "").contains("GoldenMonkeyCodexHook") } }
                if !already { entries.append(["hooks": [["type": "command", "command": command, "timeout": 3]]]); hooks[event] = entries }
            }
            root["description"] = root["description"] ?? "Golden monkey desktop pet integrations"
            root["hooks"] = hooks
            let data = try JSONSerialization.data(withJSONObject: root, options: [.prettyPrinted, .sortedKeys])
            try data.write(to: config, options: .atomic)
            alert("Codex 联动已安装", "请打开 Codex 的 Hooks 设置，审核并信任新加入的金丝猴 Hook。")
        } catch { alert("安装失败", error.localizedDescription) }
    }

    private func alert(_ title: String, _ message: String) { let value = NSAlert(); value.messageText = title; value.informativeText = message; value.alertStyle = title.contains("失败") ? .warning : .informational; value.runModal() }
    deinit { timers.forEach { $0.invalidate() } }
}

private final class AppDelegate: NSObject, NSApplicationDelegate {
    private var window: NSWindow?
    func applicationDidFinishLaunching(_ notification: Notification) {
        do {
            let view = try PetView()
            let frame = NSRect(origin: .zero, size: view.frame.size)
            let value = NSWindow(contentRect: frame, styleMask: [.borderless], backing: .buffered, defer: false)
            value.contentView = view; value.isOpaque = false; value.backgroundColor = .clear; value.hasShadow = false; value.level = .floating
            value.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]; value.isMovableByWindowBackground = false; value.acceptsMouseMovedEvents = true
            let area = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1200, height: 800)
            value.setFrameOrigin(CGPoint(x: area.midX - frame.width / 2, y: area.midY - frame.height / 2))
            value.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true); window = value
        } catch {
            let alert = NSAlert(error: error); alert.runModal(); NSApp.terminate(nil)
        }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
