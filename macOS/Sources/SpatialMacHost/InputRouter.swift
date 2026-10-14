import AppKit
import ApplicationServices
import SpatialCore

// macOS has ONE conventional focus and pointer. Spatial panels do not create
// independently focused macOS sessions. Fail closed if a captured window moved.
@MainActor final class InputRouter {
    private var heldButtons=Set<Int>()
    private var lastPoint=CGPoint.zero
    private var focusedSource:String?
    private var dragStream:UInt32?
    func releaseAll() { releaseButtons(clearFocus:true) }
    private func releaseButtons(clearFocus:Bool) {
        for b in heldButtons {
            CGEvent(mouseEventSource:nil,mouseType:b==0 ? .leftMouseUp:.rightMouseUp,
                    mouseCursorPosition:lastPoint,mouseButton:b==0 ? .left:.right)?.post(tap:.cghidEventTap)
        }
        heldButtons.removeAll();dragStream=nil;if clearFocus {focusedSource=nil}
    }
    func apply(_ m:Message,handle:CaptureHandle?) throws {
        guard AXIsProcessTrusted(),InputValidation.isValid(m) else{throw InputFailure.denied}
        if m.type=="launch" {
            guard let id=m.appID,let url=NSWorkspace.shared.urlForApplication(withBundleIdentifier:id) else{throw InputFailure.denied}
            NSWorkspace.shared.openApplication(at:url,configuration:NSWorkspace.OpenConfiguration(),completionHandler:{_,_ in})
            return
        }
        if m.type=="pointer",m.action=="up" {releaseButtons(clearFocus:false);return}
        guard let h=handle,h.generation==m.generation else{throw InputFailure.stale}
        if let ds=dragStream,ds != m.streamID {throw InputFailure.dragInProgress}
        let source=h.source
        // Drag UP is always allowed to release after a geometry change.
        
        if source.kind=="window" {
            guard let id=UInt32(source.id.split(separator:":").last ?? ""),
                  let current=windowBounds(id),current.approximatelyEquals(source.bounds) else {
                releaseAll();throw InputFailure.moved
            }
            let front=NSWorkspace.shared.frontmostApplication?.processIdentifier==source.pid
            if !front {
                if m.action=="down" {raise(source)}
                throw InputFailure.focusPending
            }
            if m.type=="text" || m.type=="shortcut" {
                guard focusedSource==source.id,frontmostAXWindowMatches(source) else{throw InputFailure.focusPending}
            }
        } else if m.type=="text" || m.type=="shortcut" {
            guard focusedSource==source.id else{throw InputFailure.focusPending}
        }
        if m.type=="pointer" {
            guard let u=m.u,let v=m.v,let p=source.bounds.point(u:u,v:v) else{throw InputFailure.denied}
            let point=CGPoint(x:p.0,y:p.1)
            if source.kind=="window",!pointHitsWindow(point,source:source) {
                if m.action=="down" {raise(source)}
                throw InputFailure.occluded
            }
            lastPoint=point
            let button=m.button ?? 0
            switch m.action {
            case "move":
                let eventType:CGEventType=heldButtons.contains(0) ? .leftMouseDragged:
                    (heldButtons.contains(1) ? .rightMouseDragged:.mouseMoved)
                CGEvent(mouseEventSource:nil,mouseType:eventType,mouseCursorPosition:point,
                        mouseButton:heldButtons.contains(1) ? .right:.left)?.post(tap:.cghidEventTap)
            case "down":
                guard !heldButtons.contains(button) else{return}
                heldButtons.insert(button);dragStream=m.streamID;focusedSource=source.id
                CGEvent(mouseEventSource:nil,mouseType:button==0 ? .leftMouseDown:.rightMouseDown,
                        mouseCursorPosition:point,mouseButton:button==0 ? .left:.right)?.post(tap:.cghidEventTap)
            case "scroll":
                let e=CGEvent(scrollWheelEvent2Source:nil,units:.pixel,wheelCount:2,
                              wheel1:Int32(m.dy ?? 0),wheel2:Int32(m.dx ?? 0),wheel3:0)
                e?.location=point;e?.post(tap:.cghidEventTap)
            default:break
            }
        } else if m.type=="text",let text=m.text {
            // Does not alter the clipboard. Review text in VR before sending.
            // A single Unicode event avoids splitting UTF-16 surrogate pairs.
            let utf16=Array(text.utf16)
            for isDown in [true,false] {
                let event=CGEvent(keyboardEventSource:nil,virtualKey:0,keyDown:isDown)
                utf16.withUnsafeBufferPointer { buffer in
                    if let base=buffer.baseAddress {event?.keyboardSetUnicodeString(stringLength:buffer.count,unicodeString:base)}
                }
                event?.post(tap:.cghidEventTap)
            }
        } else if m.type=="shortcut",let action=m.action {
            let mapping:[String:(CGKeyCode,CGEventFlags)]=[
                "copy":(8,.maskCommand),"paste":(9,.maskCommand),"undo":(6,.maskCommand),
                "redo":(6,[.maskCommand,.maskShift]),"find":(3,.maskCommand),"select_all":(0,.maskCommand),
                "escape":(53,[]),"enter":(36,[]),"backspace":(51,[]),"tab":(48,[])]
            guard let (key,flags)=mapping[action] else{throw InputFailure.denied}
            for down in [true,false] {
                let e=CGEvent(keyboardEventSource:nil,virtualKey:key,keyDown:down)
                e?.flags=flags;e?.post(tap:.cghidEventTap)
            }
        }
    }
    private func windowBounds(_ id:CGWindowID)->RectDTO? {
        guard let list=CGWindowListCopyWindowInfo(.optionIncludingWindow,id) as? [[String:Any]],
              let info=list.first,let dict=info[kCGWindowBounds as String] as? CFDictionary,
              let rect=CGRect(dictionaryRepresentation:dict) else{return nil}
        return RectDTO(rect)
    }
    private func pointHitsWindow(_ point:CGPoint,source:SourceDTO)->Bool {
        guard let list=CGWindowListCopyWindowInfo(.optionOnScreenOnly,kCGNullWindowID) as? [[String:Any]] else{return false}
        for item in list {
            guard (item[kCGWindowAlpha as String] as? Double ?? 1)>0.01,
                  let bounds=item[kCGWindowBounds as String] as? CFDictionary,
                  let rect=CGRect(dictionaryRepresentation:bounds),rect.contains(point) else{continue}
            guard let id=item[kCGWindowNumber as String] as? UInt32 else{return false}
            return source.id=="window:\(id)"
        }
        return false
    }
    private func axWindows(_ source:SourceDTO)->[AXUIElement] {
        let app=AXUIElementCreateApplication(source.pid);var value:CFTypeRef?
        guard AXUIElementCopyAttributeValue(app,kAXWindowsAttribute as CFString,&value) == .success else{return []}
        return value as? [AXUIElement] ?? []
    }
    private func matches(_ w:AXUIElement,_ source:SourceDTO)->Bool {
        var title:CFTypeRef?,pos:CFTypeRef?,size:CFTypeRef?
        AXUIElementCopyAttributeValue(w,kAXTitleAttribute as CFString,&title)
        AXUIElementCopyAttributeValue(w,kAXPositionAttribute as CFString,&pos)
        AXUIElementCopyAttributeValue(w,kAXSizeAttribute as CFString,&size)
        guard let p=pos,let s=size,CFGetTypeID(p)==AXValueGetTypeID(),CFGetTypeID(s)==AXValueGetTypeID() else{return false}
        var origin=CGPoint.zero,extent=CGSize.zero
        guard AXValueGetValue(p as! AXValue,.cgPoint,&origin),AXValueGetValue(s as! AXValue,.cgSize,&extent) else{return false}
        let rect=RectDTO(CGRect(origin:origin,size:extent))
        return (title as? String ?? "Untitled")==source.title && rect.approximatelyEquals(source.bounds,tolerance:3)
    }
    private func raise(_ source:SourceDTO) {
        let windows=axWindows(source).filter{matches($0,source)}
        guard windows.count==1 else{return}
        NSRunningApplication(processIdentifier:source.pid)?.activate(options:[.activateAllWindows])
        AXUIElementPerformAction(windows[0],kAXRaiseAction as CFString)
        AXUIElementSetAttributeValue(windows[0],kAXMainAttribute as CFString,kCFBooleanTrue)
    }
    private func frontmostAXWindowMatches(_ source:SourceDTO)->Bool {
        let app=AXUIElementCreateApplication(source.pid);var value:CFTypeRef?
        guard AXUIElementCopyAttributeValue(app,kAXFocusedWindowAttribute as CFString,&value) == .success,
              let raw=value,CFGetTypeID(raw)==AXUIElementGetTypeID() else{return false}
        return matches(raw as! AXUIElement,source)
    }
}
enum InputFailure:LocalizedError {
    case denied,stale,moved,focusPending,occluded,dragInProgress
    var errorDescription:String? {
        switch self {
        case .denied:return "Remote input is not permitted or the message is invalid."
        case .stale:return "Stale source generation. Refresh this panel before input."
        case .moved:return "Mac window moved/resized. Refresh this panel; input stopped."
        case .focusPending:return "Focus the Mac window, then click again."
        case .occluded:return "Window is obscured or has a popup. Use the full desktop panel."
        case .dragInProgress:return "Finish the current drag before switching panels."
        }
    }
}
