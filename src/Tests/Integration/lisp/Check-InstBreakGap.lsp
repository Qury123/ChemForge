;;; Scenario 1: INSTBREAK, gap mode, total width 200.
;;; The avoid side is fixed to INST (instrument line), so L1 must be cut by the two
;;; PIPE lines; same-layer crossings and unselected layers must be left alone.

(cf-report "instbreak-gap/inst-line-count"
           (= (cf-line-count "INST") 4)
           (strcat "expected 4 (3 L1 pieces + L2), actual " (itoa (cf-line-count "INST"))))

(cf-report "instbreak-gap/pipe-untouched-count"
           (= (cf-line-count "PIPE") 3)
           (strcat "expected 3, actual " (itoa (cf-line-count "PIPE"))))

(cf-report "instbreak-gap/other-layer-untouched"
           (= (cf-line-count "OTHER") 1)
           (strcat "expected 1, actual " (itoa (cf-line-count "OTHER"))))

(setq cf-h (cf-hsegs "INST" 0.0 0.000001))
(cf-report "instbreak-gap/L1-gap-intervals"
           (cf-seqs-equal cf-h '((0.0 400.0) (600.0 700.0) (900.0 1000.0)) 0.000001)
           (strcat "expected [0,400][600,700][900,1000], actual " (cf-fmt-seqs cf-h)))

(setq cf-v (cf-vsegs "INST" 200.0 0.000001))
(cf-report "instbreak-gap/L2-same-layer-kept-whole"
           (cf-seqs-equal cf-v '((-300.0 300.0)) 0.000001)
           (strcat "expected [-300,300], actual " (cf-fmt-seqs cf-v)))

(setq cf-vp (cf-vsegs "PIPE" 800.0 0.000001))
(cf-report "instbreak-gap/P2-pipe-on-pipe-kept-whole"
           (cf-seqs-equal cf-vp '((-200.0 200.0)) 0.000001)
           (strcat "expected [-200,200], actual " (cf-fmt-seqs cf-vp)))

(cf-report "instbreak-gap/pipe-on-pipe-decoy-P3-kept-whole"
           (cf-seqs-equal (cf-hsegs "PIPE" -100.0 0.000001) '((600.0 900.0)) 0.000001)
           (cf-fmt-seqs (cf-hsegs "PIPE" -100.0 0.000001)))

(cf-report "instbreak-gap/other-decoy-O1-kept-whole"
           (cf-seqs-equal (cf-vsegs "OTHER" 150.0 0.000001) '((-300.0 300.0)) 0.000001)
           (cf-fmt-seqs (cf-vsegs "OTHER" 150.0 0.000001)))

(cf-finish)
