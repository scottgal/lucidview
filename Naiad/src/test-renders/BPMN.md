# BPMN Diagram Examples

## Simple Order Process

```bpmn
<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="definitions1">
  <process id="orderProcess" isExecutable="false">
    <startEvent id="start" name="Order Received"/>
    <task id="review" name="Review Order"/>
    <exclusiveGateway id="gw1" name="Approved?"/>
    <task id="ship" name="Ship Order"/>
    <task id="reject" name="Send Rejection"/>
    <endEvent id="end" name="Complete"/>
    <sequenceFlow id="f1" sourceRef="start" targetRef="review"/>
    <sequenceFlow id="f2" sourceRef="review" targetRef="gw1"/>
    <sequenceFlow id="f3" sourceRef="gw1" targetRef="ship" name="Yes"/>
    <sequenceFlow id="f4" sourceRef="gw1" targetRef="reject" name="No"/>
    <sequenceFlow id="f5" sourceRef="ship" targetRef="end"/>
    <sequenceFlow id="f6" sourceRef="reject" targetRef="end"/>
  </process>
</definitions>
```

## Pizza Delivery Process with Lanes

```bpmn
<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="pizzaDefs">
  <collaboration id="collab1">
    <participant id="pool1" name="Pizza Planet" processRef="pizzaProcess"/>
  </collaboration>
  <process id="pizzaProcess" isExecutable="false">
    <laneSet>
      <lane id="customer" name="Hungry Customer">
        <flowNodeRef>orderStart</flowNodeRef>
        <flowNodeRef>placeOrder</flowNodeRef>
        <flowNodeRef>receiveFood</flowNodeRef>
        <flowNodeRef>eatEnd</flowNodeRef>
      </lane>
      <lane id="kitchen" name="Kitchen of Chaos">
        <flowNodeRef>makeFood</flowNodeRef>
        <flowNodeRef>qualityCheck</flowNodeRef>
        <flowNodeRef>dropOnFloor</flowNodeRef>
        <flowNodeRef>fiveSecondRule</flowNodeRef>
      </lane>
      <lane id="delivery" name="Delivery Driver">
        <flowNodeRef>grabOrder</flowNodeRef>
        <flowNodeRef>getInCar</flowNodeRef>
        <flowNodeRef>deliver</flowNodeRef>
      </lane>
    </laneSet>
    <startEvent id="orderStart" name="Stomach Growls"/>
    <userTask id="placeOrder" name="Order 47 Pizzas"/>
    <userTask id="makeFood" name="Prepare Pizza"/>
    <exclusiveGateway id="qualityCheck" name="Dropped it?"/>
    <task id="dropOnFloor" name="Pick Up Off Floor"/>
    <exclusiveGateway id="fiveSecondRule" name="5 Second Rule?"/>
    <task id="grabOrder" name="Grab Order"/>
    <task id="getInCar" name="Play Car Tetris"/>
    <userTask id="deliver" name="Ring Doorbell 47 Times"/>
    <task id="receiveFood" name="Receive Cold Pizza"/>
    <endEvent id="eatEnd" name="Happiness Achieved"/>
    <sequenceFlow id="sf1" sourceRef="orderStart" targetRef="placeOrder"/>
    <sequenceFlow id="sf2" sourceRef="placeOrder" targetRef="makeFood"/>
    <sequenceFlow id="sf3" sourceRef="makeFood" targetRef="qualityCheck"/>
    <sequenceFlow id="sf4" sourceRef="qualityCheck" targetRef="dropOnFloor" name="Yes"/>
    <sequenceFlow id="sf5" sourceRef="qualityCheck" targetRef="grabOrder" name="No"/>
    <sequenceFlow id="sf6" sourceRef="dropOnFloor" targetRef="fiveSecondRule"/>
    <sequenceFlow id="sf7" sourceRef="fiveSecondRule" targetRef="grabOrder" name="Still good!"/>
    <sequenceFlow id="sf8" sourceRef="fiveSecondRule" targetRef="makeFood" name="Start over"/>
    <sequenceFlow id="sf9" sourceRef="grabOrder" targetRef="getInCar"/>
    <sequenceFlow id="sf10" sourceRef="getInCar" targetRef="deliver"/>
    <sequenceFlow id="sf11" sourceRef="deliver" targetRef="receiveFood"/>
    <sequenceFlow id="sf12" sourceRef="receiveFood" targetRef="eatEnd"/>
  </process>
</definitions>
```

## Startup Fundraising Process

```bpmn
<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="startupDefs">
  <process id="fundraising" isExecutable="false">
    <startEvent id="idea" name="Have Idea in Shower"/>
    <task id="pitch" name="Create Pitch Deck"/>
    <task id="addAI" name="Add AI to Everything"/>
    <task id="addBlockchain" name="Add Blockchain Too"/>
    <parallelGateway id="pgw1" name="+"/>
    <task id="cold1" name="Email 500 VCs"/>
    <task id="cold2" name="Slide Into DMs"/>
    <task id="cold3" name="LinkedIn Cringe Post"/>
    <parallelGateway id="pgw2" name="+"/>
    <exclusiveGateway id="funded" name="Got Funding?"/>
    <task id="celebrate" name="Buy Standing Desk"/>
    <task id="pivot" name="Pivot to Consulting"/>
    <endEvent id="success" name="IPO (lol)"/>
    <endEvent id="failure" name="Back to Day Job"/>
    <sequenceFlow id="f1" sourceRef="idea" targetRef="pitch"/>
    <sequenceFlow id="f2" sourceRef="pitch" targetRef="addAI"/>
    <sequenceFlow id="f3" sourceRef="addAI" targetRef="addBlockchain"/>
    <sequenceFlow id="f4" sourceRef="addBlockchain" targetRef="pgw1"/>
    <sequenceFlow id="f5" sourceRef="pgw1" targetRef="cold1"/>
    <sequenceFlow id="f6" sourceRef="pgw1" targetRef="cold2"/>
    <sequenceFlow id="f7" sourceRef="pgw1" targetRef="cold3"/>
    <sequenceFlow id="f8" sourceRef="cold1" targetRef="pgw2"/>
    <sequenceFlow id="f9" sourceRef="cold2" targetRef="pgw2"/>
    <sequenceFlow id="f10" sourceRef="cold3" targetRef="pgw2"/>
    <sequenceFlow id="f11" sourceRef="pgw2" targetRef="funded"/>
    <sequenceFlow id="f12" sourceRef="funded" targetRef="celebrate" name="Miraculously Yes"/>
    <sequenceFlow id="f13" sourceRef="funded" targetRef="pivot" name="Obviously No"/>
    <sequenceFlow id="f14" sourceRef="celebrate" targetRef="success"/>
    <sequenceFlow id="f15" sourceRef="pivot" targetRef="failure"/>
  </process>
</definitions>
```

## IT Support Workflow

```bpmn
<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="itDefs">
  <collaboration id="itCollab">
    <participant id="userPool" name="User (Victim)" processRef="userProc"/>
    <participant id="itPool" name="IT Support (Heroes)" processRef="itProc"/>
    <messageFlow id="mf1" sourceRef="submitTicket" targetRef="receiveTicket" name="Desperate plea"/>
    <messageFlow id="mf2" sourceRef="askRestart" targetRef="tryRestart" name="Have you tried..."/>
  </collaboration>
  <process id="userProc" isExecutable="false">
    <startEvent id="broken" name="Something Broke"/>
    <task id="panic" name="Panic Quietly"/>
    <userTask id="submitTicket" name="Submit Ticket"/>
    <task id="tryRestart" name="Restart Everything"/>
    <exclusiveGateway id="fixed" name="Fixed?"/>
    <endEvent id="userHappy" name="Back to Work"/>
    <endEvent id="userSad" name="Use Phone Instead"/>
    <sequenceFlow id="uf1" sourceRef="broken" targetRef="panic"/>
    <sequenceFlow id="uf2" sourceRef="panic" targetRef="submitTicket"/>
    <sequenceFlow id="uf3" sourceRef="submitTicket" targetRef="tryRestart"/>
    <sequenceFlow id="uf4" sourceRef="tryRestart" targetRef="fixed"/>
    <sequenceFlow id="uf5" sourceRef="fixed" targetRef="userHappy" name="Yes!"/>
    <sequenceFlow id="uf6" sourceRef="fixed" targetRef="userSad" name="Nope"/>
  </process>
  <process id="itProc" isExecutable="false">
    <startEvent id="receiveTicket" name="Ticket Arrives"/>
    <task id="readTicket" name="Read Incomprehensible Description"/>
    <task id="askRestart" name="Ask If They Restarted"/>
    <task id="closeTicket" name="Close as Won't Fix"/>
    <endEvent id="lunchTime" name="Go to Lunch"/>
    <sequenceFlow id="if1" sourceRef="receiveTicket" targetRef="readTicket"/>
    <sequenceFlow id="if2" sourceRef="readTicket" targetRef="askRestart"/>
    <sequenceFlow id="if3" sourceRef="askRestart" targetRef="closeTicket"/>
    <sequenceFlow id="if4" sourceRef="closeTicket" targetRef="lunchTime"/>
  </process>
</definitions>
```
