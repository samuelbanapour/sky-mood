import SwiftUI

struct ContentView: View {
    @StateObject private var model = WeatherModel()

    var body: some View {
        ScrollView {
            VStack(spacing: 6) {
                if let reading = model.reading {
                    let desc = WeatherCodes.describe(reading.code)

                    Text(desc.emoji)
                        .font(.system(size: 40))

                    Text("\(Int(reading.tempC.rounded()))°")
                        .font(.system(size: 34, weight: .semibold))

                    Text(desc.label)
                        .font(.footnote)
                        .foregroundStyle(.secondary)

                    if !model.placeName.isEmpty {
                        Text(model.placeName)
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    }

                    HStack(spacing: 10) {
                        Label("\(Int(reading.highC.rounded()))°", systemImage: "arrow.up")
                        Label("\(Int(reading.lowC.rounded()))°", systemImage: "arrow.down")
                    }
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .padding(.top, 2)
                } else if model.isLoading {
                    ProgressView()
                    Text("Getting weather…")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                } else if let err = model.errorText {
                    Text(err)
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                    Button("Retry") { model.start() }
                        .font(.caption2)
                }
            }
            .padding(.vertical, 8)
        }
        .onAppear { model.start() }
    }
}
