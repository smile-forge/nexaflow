// Every test in this assembly launches Nexaflow.exe and drives the real mouse, so none of them may run
// beside another. Declared once here rather than per class: [DoNotParallelize] on a base class is honoured
// by derived classes, but stating it at assembly level makes it true for anything added later without the
// author having to know. Serialisation ACROSS assemblies is a separate problem, handled by UiTestGate.
[assembly: DoNotParallelize]
