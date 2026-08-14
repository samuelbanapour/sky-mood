import SwiftUI

/// Lists the phone's saved places (pushed alongside the current weather over
/// WatchConnectivity) so the watch can switch cities without its own search UI.
struct PlacesView: View {
    @ObservedObject var model: WeatherModel

    var body: some View {
        Group {
            if model.places.isEmpty {
                Text("No saved places yet — open Sky Mood on your phone.")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .padding()
            } else {
                List(model.places) { place in
                    Button {
                        model.selectPlace(place)
                    } label: {
                        HStack {
                            Text(place.name)
                                .lineLimit(1)
                                .truncationMode(.tail)
                            Spacer(minLength: 8)
                            Text("\(WeatherReading.display(place.tempC, fahrenheit: model.isFahrenheit))°")
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                                .layoutPriority(1)
                        }
                    }
                }
            }
        }
        .navigationTitle("Places")
    }
}
