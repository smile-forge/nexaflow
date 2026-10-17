# Serial

A terminal for any COM port: a USB serial adapter, a development board, a modem, a virtual port.

---

## Connecting
- Choose the port: each is listed with the device behind it. [Show me](locate:Serial_Port)
- Set the line to match the device — baud rate, data bits, parity, stop bits and flow control — then [Connect](locate:Serial_Connect). The settings stay fixed while connected, because both ends must agree.
- [DTR](locate:Serial_Dtr) and [RTS](locate:Serial_Rts) set the two control lines. Many boards reset when DTR changes.
- If the device is unplugged while connected, the page says so and closes the port.

## The transcript
- What arrives is shown as it comes, one line per line the device sends; what you send is shown too. ← came in, → went out. [Show me](locate:Serial_Transcript)
- [Show hex](locate:Serial_ShowHex) shows the bytes instead of the text; [Times](locate:Serial_Times) shows when each line arrived, to the millisecond.
- The transcript follows new lines unless you have scrolled up to look back.
- [Save…](locate:Serial_Save) writes the transcript, text and hex, to a file.

## Sending
- Type in the line at the bottom and press Enter. [Show me](locate:Serial_SendText)
- [The line ending](locate:Serial_LineEnding) is added after text: none, CR, LF or CR LF.
- With [Hex](locate:Serial_SendHex) on, what you type is sent as bytes: `02 10 FF`.
- The first send on each connection asks first: what you send goes straight to the device.

## Asking the assistant
- The assistant sees the port, its settings and the end of the transcript, and can read more of it. Connecting and sending reach the device, so you approve each; it can wait for the reply to what it sent.
