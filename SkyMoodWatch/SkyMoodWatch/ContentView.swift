import SwiftUI

struct ContentView: View {
    @StateObject private var model = WeatherModel()

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 6) {
                    if let reading = model.reading {
                        let desc = WeatherCodes.describe(reading.code)

                        Text(desc.emoji)
                            .font(.system(size: 40))

                        Text("\(WeatherReading.display(reading.tempC, fahrenheit: model.isFahrenheit))°")
                            .font(.system(size: 34, weight: .semibold))

                        Text(desc.label)
                            .font(.footnote)
                            .foregroundStyle(.secondary)

                        if !model.placeName.isEmpty {
                            Text(model.placeName)
                                .font(.caption2)
                                .foregroundStyle(.secondary)
                                .multilineTextAlignment(.center)
                                .lineLimit(2)
                                .minimumScaleFactor(0.75)
                        }

                        // Location's own local time, not the watch's — re-renders every 30s via
                        // TimelineView so it stays live without a manually managed Timer.
                        TimelineView(.periodic(from: .now, by: 30)) { _ in
                            Text(reading.localTimeText())
                                .font(.caption2)
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                                .minimumScaleFactor(0.8)
                        }

                        HStack(spacing: 10) {
                            Label("\(WeatherReading.display(reading.highC, fahrenheit: model.isFahrenheit))°", systemImage: "arrow.up")
                                .lineLimit(1)
                            Label("\(WeatherReading.display(reading.lowC, fahrenheit: model.isFahrenheit))°", systemImage: "arrow.down")
                                .lineLimit(1)
                        }
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .minimumScaleFactor(0.8)
                        .padding(.top, 2)

                        Text("Feels like \(WeatherReading.display(reading.feelsLikeC, fahrenheit: model.isFahrenheit))°")
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .minimumScaleFactor(0.8)

                        Text("UV \(Int(reading.uvIndex.rounded())) · \(WeatherReading.uvBand(reading.uvIndex))")
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .minimumScaleFactor(0.8)

                        if !reading.daily.isEmpty {
                            ScrollView(.horizontal, showsIndicators: false) {
                                HStack(spacing: 12) {
                                    ForEach(reading.daily) { day in
                                        VStack(spacing: 2) {
                                            Text(day.day)
                                                .font(.caption2)
                                                .foregroundStyle(.secondary)
                                                .lineLimit(1)
                                                .minimumScaleFactor(0.7)
                                            Text(WeatherCodes.describe(day.code).emoji)
                                                .font(.system(size: 16))
                                            Text("\(WeatherReading.display(day.highC, fahrenheit: model.isFahrenheit))°")
                                                .font(.caption2)
                                                .lineLimit(1)
                                                .minimumScaleFactor(0.8)
                                            Text("\(WeatherReading.display(day.lowC, fahrenheit: model.isFahrenheit))°")
                                                .font(.caption2)
                                                .foregroundStyle(.secondary)
                                                .lineLimit(1)
                                                .minimumScaleFactor(0.8)
                                        }
                                    }
                                }
                                .padding(.horizontal, 4)
                            }
                            .padding(.top, 4)
                        }
                    } else if model.isLoading {
                        ProgressView()
                        Text("Getting weather…")
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    } else if let err = model.errorText {
                        Text(err)
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                        Button("Retry") { model.refresh() }
                            .font(.caption2)
                    }
                }
                .padding(.vertical, 8)
            }
            .navigationTitle("Sky Mood")
            .toolbar {
                ToolbarItem(placement: .automatic) {
                    Button {
                        model.refresh()
                    } label: {
                        Image(systemName: "arrow.clockwise")
                    }
                }
                ToolbarItem(placement: .automatic) {
                    NavigationLink {
                        PlacesView(model: model)
                    } label: {
                        Image(systemName: "list.bullet")
                    }
                }
            }
            .onAppear { model.refresh() }
        }
    }
}
