# Markdown — Diagrams: planning & time

Diagrams for what happens, in what order, and who it is for: requirements, Gantt charts, git graphs, kanban boards, mindmaps, timelines and user journeys.

---

## Requirement diagram

SysML-style requirements and the elements that meet them, with what holds between them — satisfy, verify, derive, contain.

````markdown
```mermaid
requirementDiagram
    requirement render_req {
        id: 1
        text: Render markdown natively
        risk: low
        verifymethod: test
    }
    designConstraint no_browser {
        id: 1.1
        text: No embedded browser
        risk: medium
        verifymethod: inspection
    }
    element renderer {
        type: component
        docref: Visuals.Text
    }
    render_req - contains -> no_browser
    renderer - satisfies -> render_req
```
````

![A requirement diagram: an element satisfying a requirement, which contains a design constraint](images/markdown/mermaid-requirement.png)

---

## Gantt chart

Project schedules with sections, dependencies (`after`, `until`), task states (`done` / `active` / `crit`), milestones,
vertical markers and excluded days. Task and section names are typed into where they are drawn.

````markdown
```mermaid
gantt
    title Project timeline
    dateFormat YYYY-MM-DD
    excludes weekends
    section Design
    Spec           :done,   des1, 2024-01-01, 2024-01-07
    Mockups        :active, des2, 2024-01-08, 5d
    section Build
    Implementation :crit,   b1, after des2, 10d
    Testing        :        b2, after b1, 4d
    Release        :milestone, r1, after b2, 0d
    Code freeze    :vert,   2024-01-22, 1d
```
````

![A Gantt chart with sections and dependency-timed bars](images/markdown/mermaid-gantt.png)

---

## Git graph

Commit history across branches, with merges.

````markdown
```mermaid
gitGraph
    commit
    branch develop
    commit
    commit
    checkout main
    merge develop
    commit
```
````

![A git graph with a branch and a merge](images/markdown/mermaid-gitgraph.png)

---

## Kanban board

Columns of cards with priority, assignee and ticket metadata — drawn as a real board.

````markdown
```mermaid
kanban
  Todo
    [Write the showcase doc]
    a[Add the Venn diagram]@{ priority: 'High' }
  [In progress]
    b[Polish the renderers]@{ assigned: 'team', priority: 'Very High' }
  Done
    c[Ship ER diagrams]@{ ticket: NX-42 }
```
````

![A kanban board with priority and ticket chips](images/markdown/mermaid-kanban.png)

---

## Mindmap

Free-form hierarchies branching out from a central idea.

````markdown
```mermaid
mindmap
  root((Nexaflow))
    Markdown
      CommonMark
      Extensions
      Mermaid
    Files
      Explorer
      Editors
    AI
      Chat
      Tools
```
````

![A mindmap radiating from a central node](images/markdown/mermaid-mindmap.png)

---

## Timeline

Periods along a spine, each with its events stacked beneath; sections band the periods they group.

````markdown
```mermaid
timeline
    title History of Social Media Platform
    2002 : LinkedIn
    2004 : Facebook
         : Google
    2005 : YouTube
    2006 : Twitter
```
````

![A timeline of periods on a spine with events stacked beneath each](images/markdown/mermaid-timeline.png)

---

## User journey

Scored steps of a task: a face per score floats higher for a better experience, and each actor gets a colour.

````markdown
```mermaid
journey
    title My working day
    section Go to work
      Make tea: 5: Me
      Go upstairs: 3: Me
      Do work: 1: Me, Cat
    section Go home
      Go downstairs: 5: Me
      Sit down: 5: Me
```
````

![A user journey with section bands, scored faces and actor dots](images/markdown/mermaid-journey.png)

---

More diagram types in [Diagrams](help:MarkdownDiagrams). Back to [Markdown](help:Markdown).
