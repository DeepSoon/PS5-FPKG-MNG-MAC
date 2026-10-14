import SwiftUI

// Design system inspirado na referência: cards bem arredondados,
// verde-lima sobre superfícies escuras, botões pill, toggles.
// Made by Leite.
enum DS {
    static let lime = Color(red: 0.78, green: 0.949, blue: 0.286) // #C7F24A
    static let limeInk = Color(red: 0.16, green: 0.20, blue: 0.05)
    static let cardRadius: CGFloat = 22

    static func page(_ dark: Bool) -> Color {
        dark ? Color(red: 0.078, green: 0.082, blue: 0.094) : Color(red: 0.925, green: 0.925, blue: 0.945)
    }
    static func card(_ dark: Bool) -> Color {
        dark ? Color(red: 0.118, green: 0.122, blue: 0.137) : .white
    }
    static func side(_ dark: Bool) -> Color {
        dark ? Color(red: 0.078, green: 0.082, blue: 0.094) : Color(red: 0.925, green: 0.925, blue: 0.945)
    }
    static func field(_ dark: Bool) -> Color {
        dark ? Color(red: 0.165, green: 0.173, blue: 0.192) : Color(red: 0.945, green: 0.945, blue: 0.957)
    }
    static func ink(_ dark: Bool) -> Color {
        dark ? .white : Color(red: 0.11, green: 0.11, blue: 0.13)
    }
    static func inkText(_ dark: Bool) -> Color {
        dark ? Color(red: 0.08, green: 0.08, blue: 0.09) : .white
    }
    static func muted(_ dark: Bool) -> Color {
        dark ? Color(red: 0.66, green: 0.67, blue: 0.70) : Color(red: 0.42, green: 0.42, blue: 0.45)
    }
    static func track(_ dark: Bool) -> Color {
        dark ? Color(red: 0.165, green: 0.173, blue: 0.192) : .white
    }
}

// Botão primário pill (preto no claro / branco no escuro), área de clique total.
struct DSPillPrimary: ButtonStyle {
    let dark: Bool
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .padding(.horizontal, 20)
            .frame(minHeight: 38)
            .background(DS.ink(dark))
            .foregroundColor(DS.inkText(dark))
            .clipShape(Capsule())
            .opacity(configuration.isPressed ? 0.82 : 1.0)
            .scaleEffect(configuration.isPressed ? 0.98 : 1.0)
            .contentShape(Capsule())
    }
}

// Botão secundário pill com contorno.
struct DSGhost: ButtonStyle {
    let dark: Bool
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .medium))
            .padding(.horizontal, 16)
            .frame(minHeight: 36)
            .background(Color.clear)
            .foregroundColor(dark ? .white : Color(red: 0.11, green: 0.11, blue: 0.13))
            .overlay(Capsule().stroke(dark ? Color.white.opacity(0.35) : Color.black.opacity(0.25), lineWidth: 1))
            .opacity(configuration.isPressed ? 0.7 : 1.0)
            .contentShape(Capsule())
    }
}

// Campo de texto preenchido e arredondado (estilo da referência).
struct DSField: ViewModifier {
    let dark: Bool
    func body(content: Content) -> some View {
        content
            .textFieldStyle(.plain)
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
            .background(DS.field(dark))
            .cornerRadius(14)
    }
}

extension View {
    func dsField(_ dark: Bool) -> some View { modifier(DSField(dark: dark)) }
}

// Segmentado custom em cápsula (trilho + pill selecionada), hit area de 44pt.
struct DSSegmented<T: Hashable>: View {
    let dark: Bool
    let options: [(T, String)]
    let selection: Binding<T>
    var body: some View {
        HStack(spacing: 2) {
            ForEach(options, id: \.0) { opt in
                Button(opt.1) { selection.wrappedValue = opt.0 }
                    .font(.system(size: 12, weight: .semibold))
                    .frame(maxWidth: .infinity, minHeight: 44)
                    .background(selection.wrappedValue == opt.0 ? DS.ink(dark) : Color.clear)
                    .foregroundColor(selection.wrappedValue == opt.0 ? DS.inkText(dark) : DS.muted(dark))
                    .clipShape(Capsule())
                    .contentShape(Capsule())
                    .buttonStyle(.plain)
            }
        }
        .padding(4)
        .background(DS.track(dark))
        .clipShape(Capsule())
        .overlay(Capsule().stroke(Color.primary.opacity(0.06), lineWidth: 1))
    }
}

// Toggle no estilo da referência (trilho pill, lima quando ligado).
struct DSToggle: View {
    let dark: Bool
    let isOn: Binding<Bool>
    var body: some View {
        Toggle("", isOn: isOn)
            .toggleStyle(.switch)
            .tint(DS.lime)
            .labelsHidden()
            .frame(minWidth: 51, minHeight: 31)
            .contentShape(Rectangle())
    }
}
