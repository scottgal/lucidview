# Wireframe Skin Pack Examples

All examples use standard mermaid flowchart syntax. The `%% naiad:` directives
are mermaid `%%` comments — mermaid.js ignores them and renders normal shapes.
Naiad reads them and applies the wireframe skin shapes.

## Dating App for Cats

```mermaid
%% naiad: skinPack=wireframe
%% naiad: shapes nav=navbar, card1=card, card2=card, card3=card, hiss=button, purr=button
flowchart TD
    nav["PurrMatch — Browse — Messages — My Profile"]
    subgraph main["Main Feed"]
        card1["Mr. Whiskers\nAge: 3\nLikes: Tuna, Naps, Boxes"]
        card2["Princess Fluffybottom\nAgeNO : 2\nLikes: Laser Pointers"]
        card3["Sir Meowsalot\nAge: 5\nLikes: Knocking Things Off Tables"]
    end
    subgraph actions["Actions"]
        hiss["HISS"]
        purr["PURR"]
    end
    nav --> main
    main --> actions
    card1 --> hiss
    card2 --> purr
    card3 --> purr
```

## Startup Pitch Deck Wireframe

```mermaid
%% naiad: skinPack=wireframe
%% naiad: shapes brow=browser, logo=avatar, tagline=card, cta=button
%% naiad: shapes f1=card, f2=card, f3=card
%% naiad: shapes vol=slider, rye=toggle, buy=button
flowchart TD
    brow["InvestInMyNFTToaster.io"]
    subgraph hero["Hero Section"]
        logo["LOGO"]
        tagline["We're Disrupting Toast\nWith Blockchain"]
        cta["INVEST NOW"]
    end
    subgraph features["Features"]
        f1["AI-Powered\nBrowning Algorithm"]
        f2["Each Slice Gets\nIts Own Token"]
        f3["Burns Money\nAND Bread"]
    end
    subgraph pricing["Pricing"]
        vol["Bread Commitment Level"]
        rye["Enable Rye Mode"]
        buy["Pre-Order: $4,999"]
    end
    brow --> hero
    hero --> features
    features --> pricing
    logo --> tagline
    tagline --> cta
```

## Evil Villain's Control Panel

```mermaid
%% naiad: skinPack=wireframe
%% naiad: shapes sb=sidebar, srch=searchbar, s1=badge, s2=badge, s3=progress-bar
%% naiad: shapes dd=dropdown, c1=checkbox, c2=checkbox, r1=radio, r2=radio, launch=button
flowchart LR
    sb["Menu\nDeath Ray\nShark Tank\nMonologue\nEscape Pod"]
    subgraph dash["Dashboard"]
        srch["Search victims..."]
        s1["Heroes Defeated: 0"]
        s2["Plans Foiled: 47"]
        s3["World Domination: 12%"]
    end
    subgraph controls["Doom Controls"]
        dd["Select Doomsday Device"]
        c1["Confirm Evil Laugh"]
        c2["Monologue Complete"]
        r1["Destroy Earth"]
        r2["Strongly Destroy Earth"]
        launch["ACTIVATE"]
    end
    sb --> dash
    dash --> controls
    srch --> s1
    srch --> s2
```

## Social Media for Plants

```mermaid
%% naiad: skinPack=wireframe
%% naiad: shapes nav=navbar, pic=avatar, info=card, bio=text-input
%% naiad: shapes p1=card, img=image-placeholder, p2=card
%% naiad: shapes like=button, share=button, comment=text-input
flowchart TD
    nav["Photosynthagram — Feed — Water Me — Settings"]
    subgraph profile["Plant Profile"]
        pic["Cactus Dave"]
        info["DesertDave\nSpecies: Cactus\nAge: 47 years\nWater: Never"]
        bio["Just a prickly boi living my best life"]
    end
    subgraph feed["Recent Posts"]
        p1["Just got repotted! Feeling spacious"]
        img["photo_new_pot.jpg"]
        p2["Day 12,045 of photosynthesis.\nStill love it."]
    end
    subgraph actions["Engagement"]
        like["Water"]
        share["Propagate"]
        comment["Nice leaves..."]
    end
    nav --> profile
    profile --> feed
    feed --> actions
```

## E-Commerce for Socks (Enterprise Edition)

```mermaid
%% naiad: skinPack=wireframe
%% naiad: shapes brow=browser, topnav=navbar, srch=searchbar
%% naiad: shapes c1=card, c2=card, c3=card, c4=card
%% naiad: shapes email=text-input, sz=dropdown, pop=modal, buy=button
flowchart TD
    brow["SockCorp Enterprise Pro Max Plus"]
    subgraph header["Navigation"]
        topnav["Home — Socks — More Socks — Even More Socks — Help"]
        srch["Search 14 million socks..."]
    end
    subgraph main["Product Grid"]
        c1["Argyle Sock\n$899.99"]
        c2["Plain White Sock\n$1,299.99"]
        c3["The Missing Sock\nPRICELESS"]
        c4["Quantum Sock\nBoth Clean AND Dirty"]
    end
    subgraph checkout["Checkout"]
        email["your@email.com"]
        sz["Select Foot Shape"]
        pop["Subscribe to Sock Facts?\nGet 47 emails/day about socks!\nYou cannot escape."]
        buy["SURRENDER TO SOCKS"]
    end
    brow --> header
    header --> main
    main --> checkout
    srch --> c1
    srch --> c2
```

## The Ultimate Settings Page

```mermaid
%% naiad: skinPack=wireframe
%% naiad: shapes t1=tab, t2=tab, t3=tab
%% naiad: shapes nm=text-input, th=dropdown, dk=toggle, ntf=checkbox, vol=slider
%% naiad: shapes div=divider, save=button, cancel=button
flowchart TD
    subgraph tabs["Settings Tabs"]
        t1["General"]
        t2["Advanced"]
        t3["Chaos"]
    end
    subgraph general["General Settings"]
        nm["Display Name: Captain Settings"]
        th["Theme: Existential Dread"]
        dk["Dark Mode - Soul Edition"]
        ntf["Notify me about everything"]
        vol["Notification Volume"]
    end
    subgraph footer[" "]
        div[" "]
        save["Save and Pray"]
        cancel["Abandon All Hope"]
    end
    tabs --> general
    general --> footer
    t1 --> nm
    t2 --> th
```
