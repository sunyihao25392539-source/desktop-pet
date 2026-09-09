// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "GoldenMonkeyPetMac",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "GoldenMonkeyPet", targets: ["GoldenMonkeyPet"]),
        .executable(name: "GoldenMonkeyCodexHook", targets: ["GoldenMonkeyCodexHook"])
    ],
    targets: [
        .target(name: "GoldenMonkeyShared"),
        .executableTarget(name: "GoldenMonkeyPet", dependencies: ["GoldenMonkeyShared"]),
        .executableTarget(name: "GoldenMonkeyCodexHook", dependencies: ["GoldenMonkeyShared"]),
        .testTarget(name: "GoldenMonkeySharedTests", dependencies: ["GoldenMonkeyShared"])
    ]
)
