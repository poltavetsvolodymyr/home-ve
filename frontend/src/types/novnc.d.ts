// The parts of noVNC's RFB class this app uses (the package ships no types). https://novnc.com/noVNC/docs/API.html
declare module '@novnc/novnc' {
  export default class RFB extends EventTarget {
    constructor(target: HTMLElement, url: string, options?: { wsProtocols?: string[]; shared?: boolean })
    scaleViewport: boolean
    /** CSS background around the screen when it doesn't fill the box */
    background: string
    resizeSession: boolean
    viewOnly: boolean
    disconnect(): void
    sendCtrlAltDel(): void
    sendKey(keysym: number, code: string | null, down?: boolean): void
    focus(): void
  }
}
