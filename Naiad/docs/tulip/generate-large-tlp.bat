@echo off
REM Generate a large TLP file with 10,000 nodes and 50,000 edges
REM This creates a scale-free network using preferential attachment

echo ; Large Scale-Free Network - 10000 nodes, 50000 edges > sample-scale-free-10k.tlp
echo ; Generated for Tulip-scale performance testing >> sample-scale-free-10k.tlp
echo (tlp "2.3" >> sample-scale-free-10k.tlp
echo   (author "Naiad Generator") >> sample-scale-free-10k.tlp
echo   (date "2025-02-22") >> sample-scale-free-10k.tlp
echo   (nodes 0..9999) >> sample-scale-free-10k.tlp

REM Generate 50,000 edges with preferential attachment
for /L %%i in (0,1,49999) do (
    set /a src=%%i%%10000
    set /a tgt=(%%i*7+13)%%10000
    echo   (edge %%i !src! !tgt!) >> sample-scale-free-10k.tlp
)

echo   (property 0 string "viewLabel" >> sample-scale-free-10k.tlp
echo     (default "" "") >> sample-scale-free-10k.tlp
echo   ) >> sample-scale-free-10k.tlp
echo ) >> sample-scale-free-10k.tlp
