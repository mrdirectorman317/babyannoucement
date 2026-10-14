// swift-tools-version: 5.9
import PackageDescription
var products: [Product] = [.library(name: "SpatialCore", targets: ["SpatialCore"])]
var targets: [Target] = [
    .target(name: "SpatialCore"),
    .testTarget(name: "SpatialCoreTests", dependencies: ["SpatialCore"])
]
#if os(macOS)
products.append(.executable(name: "SpatialMacHost", targets: ["SpatialMacHost"]))
targets.append(.executableTarget(name: "SpatialMacHost", dependencies: ["SpatialCore"]))
#endif
let package = Package(
    name: "SpatialMac", platforms: [.macOS(.v14)],
    products: products, targets: targets, swiftLanguageVersions: [.v5]
)
