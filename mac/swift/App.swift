import SwiftUI

// PS5 FPKG Builder — native macOS UI. Made by Leite.
// Visual: cards escuros arredondados + verde-lima + botões pill.
// Sem macros de property-wrapper (toolchain CLT): modelos @Observable + Bindings.
@main
struct FPKGApp: App {
    let state = AppState()

    var body: some Scene {
        WindowGroup {
            ShellView(state: state)
                .preferredColorScheme(state.dark ? .dark : .light)
                .frame(minWidth: 1040, minHeight: 660)
        }
        .windowStyle(.titleBar)
        .commands {
            CommandGroup(replacing: .appInfo) {
                Button("About PS5 FPKG Builder") {
                    NSApp.orderFrontStandardAboutPanel(options: [
                        .applicationName: "PS5 FPKG Builder",
                        .applicationVersion: MacPortVersion,
                    ])
                }
            }
        }
    }
}

struct ShellView: View {
    let state: AppState
    private var dark: Bool { state.dark }

    var body: some View {
        HSplitView {
            SidebarView(state: state)
                .frame(minWidth: 224, idealWidth: 236, maxWidth: 256)
            VStack(spacing: 0) {
                content
                Divider()
                StatusBarView(state: state)
            }
            .frame(minWidth: 780)
            .background(DS.page(dark))
        }
        .background(DS.side(dark))
        .onAppear { checkBackend() }
    }

    @ViewBuilder
    private var content: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 0) {
                switch state.page {
                case .reader: ReaderView(state: state)
                case .files: FilesView(state: state)
                case .build: BuildView(state: state)
                case .extractor: ExtractorView(state: state)
                case .trophy: TrophyView(state: state)
                case .about: AboutView(state: state)
                }
            }
            .padding(28)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private func checkBackend() {
        DispatchQueue.global(qos: .utility).async {
            let (rc, out, _) = runBackendSync(["version"], timeout: 20)
            DispatchQueue.main.async {
                if rc == 0 {
                    state.backendOK = true
                    state.backendStatus = "● " + state.t("backend_ok")
                    let first = out.split(separator: "\n").first.map(String.init) ?? ""
                    state.statusText = first.isEmpty ? state.t("ready") : first
                } else {
                    state.backendOK = false
                    state.backendStatus = "● " + state.t("backend_fail")
                    state.statusText = state.t("backend_fail")
                }
            }
        }
    }
}

struct SidebarView: View {
    let state: AppState
    private var dark: Bool { state.dark }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 11) {
                ZStack {
                    Circle().fill(DS.lime).frame(width: 40, height: 40)
                    Text("P5").font(.system(size: 14, weight: .bold)).foregroundColor(DS.limeInk)
                }
                VStack(alignment: .leading, spacing: 1) {
                    Text("PS5 FPKG Builder").font(.system(size: 13, weight: .bold))
                    Text("v\(MacPortVersion) · \(state.t("version_tag"))")
                        .font(.system(size: 10)).foregroundColor(DS.muted(dark))
                }
            }
            .padding(.horizontal, 16)
            .padding(.top, 20)
            .padding(.bottom, 14)

            ForEach(Page.allCases, id: \.self) { p in
                NavRow(state: state, page: p, active: state.page == p) { state.page = p }
            }
            Spacer()
            DSSegmented(dark: dark,
                        options: [(Lang.ptBR, "PT-BR"), (Lang.en, "EN")],
                        selection: bind({ state.lang }, { state.lang = $0 }))
                .padding(.horizontal, 14)
                .padding(.bottom, 10)
            HStack {
                Text(state.dark ? state.t("to_dark") : state.t("to_light"))
                    .font(.system(size: 12, weight: .medium))
                Spacer()
                DSToggle(dark: dark, isOn: bind({ state.dark }, { state.dark = $0 }))
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 6)
            VStack(alignment: .leading, spacing: 2) {
                Text("1.0.0-NG").font(.system(size: 10)).foregroundColor(DS.muted(dark))
                Text(MadeByLeite).font(.system(size: 10, weight: .semibold)).foregroundColor(DS.muted(dark))
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
        }
        .background(DS.side(dark))
    }
}

struct NavRow: View {
    let state: AppState
    let page: Page
    let active: Bool
    let go: () -> Void
    private var dark: Bool { state.dark }

    private func label(_ p: Page) -> String {
        switch p {
        case .reader: return state.t("nav_reader")
        case .files: return state.t("nav_files")
        case .build: return state.t("nav_build")
        case .extractor: return state.t("nav_extractor")
        case .trophy: return state.t("nav_trophy")
        case .about: return state.t("nav_about")
        }
    }

    var body: some View {
        Button(action: go) {
            Text(label(page))
                .font(.system(size: 13, weight: active ? .semibold : .regular))
                .foregroundColor(active ? DS.inkText(dark) : DS.muted(dark))
                .padding(.vertical, 13)
                .padding(.horizontal, 16)
                .frame(maxWidth: .infinity, minHeight: 48, alignment: .leading)
                .background(active ? DS.ink(dark) : Color.clear)
                .clipShape(Capsule())
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .padding(.horizontal, 10)
        .padding(.vertical, 2)
    }
}

struct StatusBarView: View {
    let state: AppState
    private var dark: Bool { state.dark }

    var body: some View {
        HStack(spacing: 10) {
            Text(state.statusText).font(.system(size: 11)).foregroundColor(DS.muted(dark))
                .lineLimit(1).truncationMode(.middle)
            Spacer()
            if state.progActive {
                progressText
                if state.progTotal > 0 {
                    ProgressView(value: Double(state.progDone), total: Double(state.progTotal))
                        .tint(DS.lime)
                        .frame(width: 150)
                } else {
                    ProgressView().frame(width: 60)
                }
            }
            HStack(spacing: 5) {
                Circle().fill(state.backendOK ? DS.lime : Color.red).frame(width: 8, height: 8)
                Text(state.backendOK ? state.t("backend_ok") : state.backendStatus)
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(DS.muted(dark))
            }
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 9)
        .background(DS.side(dark))
    }

    private var progressText: some View {
        let pct: Int = state.progTotal > 0 ? min(99, Int(state.progDone * 100 / state.progTotal)) : 0
        let left = max(0, state.progTotal - state.progDone)
        var eta = ""
        if state.progSpeed > 0, left > 0 {
            let s = Double(left) / state.progSpeed
            eta = s < 90 ? " · ~\(Int(s))s" : " · ~\(Int((s / 60).rounded())) min"
        }
        let s = tr("transferred", state.lang, [
            "pct": "\(pct)",
            "done": fmtMB(Double(state.progDone), lang: state.lang),
            "total": fmtMB(Double(state.progTotal), lang: state.lang),
            "speed": fmtMB(state.progSpeed, lang: state.lang),
            "left": fmtMB(Double(left), lang: state.lang),
            "eta": eta,
        ])
        return Text(s).font(.system(size: 10, design: .monospaced)).foregroundColor(DS.muted(dark))
    }
}

// MARK: - Shared page chrome

struct PageHeader: View {
    let kicker, title, desc: String
    let dark: Bool
    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(kicker).font(.system(size: 11, weight: .bold))
                .tracking(1.4)
                .foregroundColor(dark ? DS.lime : Color(red: 0.35, green: 0.45, blue: 0.05))
            Text(title).font(.system(size: 27, weight: .bold))
            if !desc.isEmpty {
                Text(desc).font(.system(size: 12)).foregroundColor(DS.muted(dark)).fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.bottom, 16)
    }
}

struct Card<Content: View>: View {
    let dark: Bool
    let content: Content
    init(dark: Bool, @ViewBuilder content: () -> Content) {
        self.dark = dark
        self.content = content()
    }
    var body: some View {
        content
            .padding(22)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(DS.card(dark))
            .cornerRadius(DS.cardRadius)
            .shadow(color: dark ? .clear : Color.black.opacity(0.05), radius: 10, y: 3)
            .padding(.bottom, 16)
    }
}

struct FieldLabel: View {
    let text: String
    let dark: Bool
    var body: some View {
        Text(text).font(.system(size: 13, weight: .semibold))
            .foregroundColor(dark ? .white : .black)
            .padding(.bottom, 5)
    }
}

struct LogView: View {
    let text: String
    let dark: Bool
    var body: some View {
        ScrollView {
            ScrollViewReader { proxy in
                Text(text.isEmpty ? " " : text)
                    .font(.system(size: 11, design: .monospaced))
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .textSelection(.enabled)
                    .padding(14)
                    .id("end")
                    .onChange(of: text) { proxy.scrollTo("end", anchor: .bottom) }
            }
        }
        .frame(minHeight: 140, maxHeight: 260)
        .background(DS.field(dark))
        .cornerRadius(16)
    }
}
