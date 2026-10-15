import Foundation
import XCTest
@testable import KrillMac

final class LoginWindowControllerTests: XCTestCase {
    @MainActor
    func testNavigationPolicyCallbacksAreRegistered() async {
        for callback in [
            "webView:decidePolicyForNavigationAction:decisionHandler:",
            "webView:decidePolicyForNavigationResponse:decisionHandler:"
        ] {
            XCTAssertTrue(LoginWindowController.instancesRespond(to: NSSelectorFromString(callback)), callback)
        }
    }
}
