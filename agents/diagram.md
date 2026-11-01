# Economy crew - agent roles and data flow

```mermaid
flowchart TD
    GDD["GDD economy section<br/>agents/context/gdd_economy.md"]
    START["Starting values<br/>agents/data/economy_start.json"]

    subgraph CREW["Economy crew - agents/run.py"]
        DIR1["Director (Sonnet)<br/>writes the task card"]
        BAL["Economy Balancer (Sonnet)<br/>proposes economy.json"]
        VAL{"Rules check<br/>(code)"}
        SIM["Economy simulation (code)<br/>buyer, swapper, releaser bots<br/>+ worst-case day, 300 runs each"]
        QA["QA Playtester (Haiku)<br/>reads the target checks,<br/>writes feedback"]
        PASS{"All 5 targets<br/>pass?"}
        DIR2["Director (Sonnet)<br/>approves or escalates"]
    end

    OUT["Assets/Data/economy.json<br/>loaded by Unity (EconomyLoader.cs)"]
    REP["agents/reports/*.md<br/>run report"]
    HUMAN["Designer<br/>(final decider)"]

    GDD --> DIR1
    DIR1 -- task card --> BAL
    START -- current values --> BAL
    BAL -- proposal --> VAL
    VAL -- invalid: problems --> QA
    VAL -- valid --> SIM
    SIM -- metrics + pass/fail --> QA
    QA --> PASS
    PASS -- "no, round < 3:<br/>QA report" --> BAL
    PASS -- "yes, or 3 rounds failed" --> DIR2
    DIR2 -- approved --> OUT
    DIR2 --> REP
    DIR2 -- escalated --> HUMAN
```

**Roles**

| Agent | Model | Reads | Produces (format) |
|---|---|---|---|
| Director | Claude Sonnet | GDD economy section, QA targets, tuning history | Task card and final decision (Markdown) |
| Economy Balancer | Claude Sonnet | Task card, current economy.json, last QA report | economy.json (JSON) |
| QA Playtester | Claude Haiku + simulation code | economy.json, simulation results, target checks | Verdict with feedback (JSON) |

The pass/fail checks are computed by the simulation, not by an agent, so the QA Playtester can explain results but cannot fake them.
