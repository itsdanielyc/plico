import AppKit
import Darwin
import Foundation
import ImageCaptureCore

private struct ScanRequest {
    let identifier: String
    let feed: String
    let paper: String
    let resolution: Int
    let colour: String
    let destination: URL
}

private final class ScannerSession: NSObject, ICDeviceBrowserDelegate, ICScannerDeviceDelegate {
    private let browser = ICDeviceBrowser()
    private let request: ScanRequest?
    private var scanner: ICScannerDevice?
    private var sessionOpen = false
    private var deviceReady = false
    private var selectingUnit = false
    private var deliveredPages = 0
    private var finishing = false

    init(request: ScanRequest?) {
        self.request = request
        super.init()
    }

    func start() {
        browser.delegate = self
        let localScanners = ICDeviceTypeMask.scanner.rawValue | ICDeviceLocationTypeMask.local.rawValue
        browser.browsedDeviceTypeMask = ICDeviceTypeMask(rawValue: localScanners) ?? .scanner
        browser.start()
        DispatchQueue.main.asyncAfter(deadline: .now() + .seconds(5)) { [weak self] in
            self?.discoveryEnded()
        }
    }

    func deviceBrowser(_ browser: ICDeviceBrowser, didAdd device: ICDevice, moreComing: Bool) {
        guard let candidate = device as? ICScannerDevice,
              let identifier = candidate.uuidString,
              let name = candidate.name else { return }
        guard let request else {
            emit(["Id": identifier, "Name": name])
            return
        }
        guard scanner == nil, identifier == request.identifier else { return }
        scanner = candidate
        candidate.delegate = self
        candidate.requestOpenSession()
    }

    func deviceBrowser(_ browser: ICDeviceBrowser, didRemove device: ICDevice, moreGoing: Bool) {
        if device === scanner { finish("The scanner disconnected.") }
    }

    func deviceBrowserDidEnumerateLocalDevices(_ browser: ICDeviceBrowser) {
        if request == nil { finish(nil) }
    }

    func didRemove(_ device: ICDevice) {
        if device === scanner { finish("The scanner disconnected.") }
    }

    func device(_ device: ICDevice, didOpenSessionWithError error: Error?) {
        if let error {
            finish(error.localizedDescription)
            return
        }
        sessionOpen = true
        selectUnitIfReady()
    }

    func deviceDidBecomeReady(_ device: ICDevice) {
        deviceReady = true
        selectUnitIfReady()
    }

    private func selectUnitIfReady() {
        guard let scanner, let request, sessionOpen, deviceReady, !selectingUnit else { return }
        selectingUnit = true
        let available = scanner.availableFunctionalUnitTypes.map { $0.uintValue }
        let flatbedAvailable = available.contains(ICScannerFunctionalUnitType.flatbed.rawValue)
        let unit: ICScannerFunctionalUnitType
        switch request.feed {
        case "flatbed": unit = .flatbed
        case "feeder", "duplex": unit = .documentFeeder
        default: unit = flatbedAvailable ? .flatbed : .documentFeeder
        }
        guard available.contains(unit.rawValue) else {
            finish("The scanner does not offer the selected paper source.")
            return
        }
        scanner.requestSelect(unit)
    }

    func scannerDevice(_ scanner: ICScannerDevice, didSelect unit: ICScannerFunctionalUnit, error: Error?) {
        if let error {
            finish(error.localizedDescription)
            return
        }
        guard let request else { return }
        if let feeder = unit as? ICScannerFunctionalUnitDocumentFeeder {
            if request.feed == "duplex" && !feeder.supportsDuplexScanning {
                finish("The scanner does not support two-sided feeder capture.")
                return
            }
            feeder.duplexScanningEnabled = request.feed == "duplex"
        }

        unit.measurementUnit = .inches
        let a4Width = 8.27
        let a4Height = 11.69
        let letterWidth = 8.5
        let letterHeight = 11.0
        let width: CGFloat = request.paper == "a4" ? a4Width : letterWidth
        let height: CGFloat = request.paper == "a4" ? a4Height : letterHeight
        let physicalSize = unit.physicalSize
        unit.scanArea = NSRect(x: 0, y: 0,
            width: physicalSize.width > 0 ? min(width, physicalSize.width) : width,
            height: physicalSize.height > 0 ? min(height, physicalSize.height) : height)
        unit.resolution = unit.supportedResolutions.min { abs($0 - request.resolution) < abs($1 - request.resolution) }
            ?? request.resolution
        if request.colour == "mono" && unit.supportedBitDepths.contains(Int(ICScannerBitDepth.depth1Bit.rawValue)) {
            unit.bitDepth = .depth1Bit
            unit.pixelDataType = .BW
        } else {
            unit.bitDepth = .depth8Bits
            unit.pixelDataType = request.colour == "colour" ? .RGB : .gray
        }

        scanner.transferMode = .fileBased
        scanner.downloadsDirectory = request.destination
        scanner.documentName = "Scan"
        scanner.documentUTI = "public.jpeg"
        scanner.requestScan()
    }

    func scannerDevice(_ scanner: ICScannerDevice, didScanTo url: URL) {
        guard let request else { return }
        let output = url.standardizedFileURL
        guard output.deletingLastPathComponent().resolvingSymlinksInPath() ==
              request.destination.standardizedFileURL.resolvingSymlinksInPath() else {
            finish("The scanner returned a page outside its output folder.")
            return
        }
        deliveredPages += 1
        emit(["Page": output.lastPathComponent])
    }

    func scannerDevice(_ scanner: ICScannerDevice, didCompleteScanWithError error: Error?) {
        finish(error?.localizedDescription ?? (deliveredPages == 0 ? "The scanner returned no pages." : nil))
    }

    func device(_ device: ICDevice, didEncounterError error: Error?) {
        if let error { finish(error.localizedDescription) }
    }

    private func discoveryEnded() {
        if request == nil { finish(nil) }
        if scanner == nil { finish("The scanner is unavailable. Refresh the device list.") }
    }

    private func finish(_ message: String?) {
        guard !finishing else { return }
        finishing = true
        let status: Int32 = message == nil ? EXIT_SUCCESS : EXIT_FAILURE
        if let message {
            FileHandle.standardError.write(Data((message + "\n").utf8))
        }
        if sessionOpen {
            scanner?.requestCloseSession()
            DispatchQueue.main.asyncAfter(deadline: .now() + .seconds(1)) { exit(status) }
        } else {
            exit(status)
        }
    }

    private func emit(_ fields: [String: String]) {
        guard let data = try? JSONSerialization.data(withJSONObject: fields) else {
            finish("The scanner returned an invalid response.")
            return
        }
        FileHandle.standardOutput.write(data)
        FileHandle.standardOutput.write(Data([10]))
    }
}

let arguments = Array(CommandLine.arguments.dropFirst())
if arguments == ["list"] {
    let session = ScannerSession(request: nil)
    session.start()
    withExtendedLifetime(session) { RunLoop.main.run() }
} else if arguments.count == 7, arguments[0] == "scan", let resolution = Int(arguments[4]), resolution > 0 {
    let request = ScanRequest(identifier: arguments[1], feed: arguments[2], paper: arguments[3],
        resolution: resolution, colour: arguments[5], destination: URL(fileURLWithPath: arguments[6], isDirectory: true))
    let session = ScannerSession(request: request)
    session.start()
    withExtendedLifetime(session) { RunLoop.main.run() }
} else {
    FileHandle.standardError.write(Data("Supply list or scan with scanner, source, paper, resolution, colour and destination.\n".utf8))
    exit(EXIT_FAILURE)
}
