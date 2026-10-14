import SwiftUI

// MARK: - AMPR (LZ4) packs (Made by Leite)

struct AmprView: View {
    let state: AppState
    private var m: AmprModel { state.ampr }

    private var levelInt: Int { Int(m.level) }
    private var modeHint: String {
        levelInt <= 0 ? "0 · \(state.t("level_fast"))"
            : levelInt <= 4 ? "\(levelInt) · \(state.t("level_fast"))"
            : "\(levelInt) · \(state.t("level_hc"))"
    }

    var body: some View {
        PageHeader(kicker: state.t("ampr_kicker"), title: state.t("ampr_title"), desc: state.t("ampr_desc"), dark: state.dark)
        Card(dark: state.dark) {
            VStack(alignment: .leading, spacing: 8) {
                DSSegmented(dark: state.dark,
                            options: [(false, state.t("src_folder")), (true, state.t("src_exfat"))],
                            selection: bind({ m.isImage }, { m.isImage = $0 }))
                    .frame(width: 340)
                FieldLabel(text: state.t("source_label"), dark: state.dark)
                HStack {
                    TextField(".exfat…", text: bind({ m.source }, { m.source = $0 }))
                        .dsField(state.dark)
                    Button(state.t("btn_browse")) { browseSrc() }
                        .buttonStyle(DSGhost(dark: state.dark))
                }
                FieldLabel(text: state.t("tmp_label"), dark: state.dark).padding(.top, 6)
                HStack {
                    TextField("", text: bind({ m.output }, { m.output = $0 }))
                        .dsField(state.dark)
                    Button(state.t("btn_browse")) {
                        if let d = pickFolder(title: state.t("tmp_label")) {
                            let base = ((m.source as NSString).deletingPathExtension as NSString).lastPathComponent
                            m.output = (d as NSString).appendingPathComponent(base.isEmpty ? "game-ampr" : base + "-ampr")
                        }
                    }
                    .buttonStyle(DSGhost(dark: state.dark))
                }
                HStack {
                    Text(state.t("level_label")).font(.system(size: 13, weight: .semibold))
                    Slider(value: bind({ m.level }, { m.level = $0 }), in: 0...12, step: 1)
                        .tint(DS.lime)
                        .frame(width: 220)
                    Text(modeHint)
                        .font(.system(size: 12, design: .monospaced))
                        .foregroundColor(DS.muted(state.dark))
                        .frame(minWidth: 170, alignment: .leading)
                }.padding(.top, 8)
                Button(state.t("btn_compress")) { compress() }
                    .buttonStyle(DSPillPrimary(dark: state.dark))
                    .disabled(state.progActive)
                    .padding(.top, 6)
            }
        }
        Card(dark: state.dark) {
            VStack(alignment: .leading, spacing: 8) {
                FieldLabel(text: state.t("ampr_packdir"), dark: state.dark)
                HStack {
                    TextField("", text: bind({ m.packDir }, { m.packDir = $0 }))
                        .dsField(state.dark)
                    Button(state.t("btn_browse")) {
                        if let d = pickFolder(title: state.t("ampr_packdir")) { m.packDir = d }
                    }
                    .buttonStyle(DSGhost(dark: state.dark))
                }
                FieldLabel(text: state.t("ampr_dest"), dark: state.dark).padding(.top, 6)
                HStack {
                    TextField("", text: bind({ m.dest }, { m.dest = $0 }))
                        .dsField(state.dark)
                    Button(state.t("btn_browse")) {
                        if let d = pickFolder(title: state.t("ampr_dest")) { m.dest = d }
                    }
                    .buttonStyle(DSGhost(dark: state.dark))
                }
                Button(state.t("btn_unpack")) { unpack() }
                    .buttonStyle(DSGhost(dark: state.dark))
                    .disabled(state.progActive)
                    .padding(.top, 6)
            }
        }
        Card(dark: state.dark) {
            VStack(alignment: .leading, spacing: 8) {
                Text(state.t("log_label")).font(.system(size: 13, weight: .semibold))
                LogView(text: m.log, dark: state.dark)
            }
        }
        .alert(state.t("err_title"), isPresented: bind({ m.showAlert }, { m.showAlert = $0 })) {
            Button("OK", role: .cancel) {}
        } message: { Text(m.alertMsg) }
        .alert("1.0.0-NG · \(MadeByLeite)", isPresented: bind({ m.showDone }, { m.showDone = $0 })) {
            Button("OK", role: .cancel) {}
        } message: { Text(m.donePath) }
    }

    private func browseSrc() {
        if m.isImage {
            if let p = pickFile(title: state.t("src_exfat"), extensions: ["exfat"]) { m.source = p }
        } else if let d = pickFolder(title: state.t("src_folder")) { m.source = d }
    }

    private func compress() {
        guard !m.source.isEmpty, !m.output.isEmpty else {
            m.alertMsg = state.t("ask_ampr_src"); m.showAlert = true; return
        }
        _ = startTask(state, ["ampr-pack", m.source, m.output, "--level", "\(levelInt)"],
                      setLog: { _ in m.log = "" },
                      appendLog: { m.log += $0 + "\n"; if m.log.count > 120_000 { m.log = String(m.log.suffix(60_000)) } },
                      status: state.t("working")) { rc, transcript in
            if rc == 0 {
                m.donePath = m.output
                m.showDone = true
                state.statusText = state.t("done")
            } else {
                m.alertMsg = String(transcript.suffix(3000)); m.showAlert = true
                state.statusText = state.t("failed")
            }
        }
    }

    private func unpack() {
        guard !m.packDir.isEmpty, !m.dest.isEmpty else {
            m.alertMsg = state.t("ask_ampr_unpack"); m.showAlert = true; return
        }
        _ = startTask(state, ["ampr-unpack", m.packDir, m.dest],
                      appendLog: { m.log += $0 + "\n"; if m.log.count > 120_000 { m.log = String(m.log.suffix(60_000)) } },
                      status: state.t("working")) { rc, transcript in
            if rc == 0 {
                m.donePath = m.dest
                m.showDone = true
                state.statusText = state.t("done")
            } else {
                m.alertMsg = String(transcript.suffix(3000)); m.showAlert = true
                state.statusText = state.t("failed")
            }
        }
    }
}
