;;; Scenario 5: PIPEBREAK endpoint rule.
;;; A branch that lands on a trunk (T junction) must NOT break the trunk -- not even when the
;;; branch end overshoots the trunk centreline by a hair (0.4 here), which is what real drawings
;;; look like when the joint was not snapped exactly.
;;; A genuine crossing in the same drawing must still be broken, so the new rule cannot silently
;;; disable breaking altogether.

;; T1: the branch only touches -> the trunk must stay one piece.
(setq cf-t1 (cf-vsegs "PIPE" 500.0 0.000001))
(cf-report "pipebreak-tjunction/T1-trunk-not-broken"
           (cf-seqs-equal cf-t1 '((-200.0 200.0)) 0.000001)
           (strcat "expected [-200,200] (no gap), actual " (cf-fmt-seqs cf-t1)))

;; T2: a real crossing -> the trunk is cut into [-200,-50] + [50,200].
(setq cf-t2 (cf-vsegs "PIPE" 1500.0 0.000001))
(cf-report "pipebreak-tjunction/T2-trunk-gap-intervals"
           (cf-seqs-equal cf-t2 '((-200.0 -50.0) (50.0 200.0)) 0.000001)
           (strcat "expected [-200,-50][50,200], actual " (cf-fmt-seqs cf-t2)))

;; Direction is V, so neither branch is the avoid side: both must be untouched.
(setq cf-t1b (cf-hsegs "PIPE" 100.0 0.000001))
(cf-report "pipebreak-tjunction/T1b-branch-untouched"
           (cf-seqs-equal cf-t1b '((499.6 900.0)) 0.000001)
           (strcat "expected [499.6,900], actual " (cf-fmt-seqs cf-t1b)))

(setq cf-t2b (cf-hsegs "PIPE" 0.0 0.000001))
(cf-report "pipebreak-tjunction/T2b-branch-untouched"
           (cf-seqs-equal cf-t2b '((1200.0 1800.0)) 0.000001)
           (strcat "expected [1200,1800], actual " (cf-fmt-seqs cf-t2b)))

(cf-report "pipebreak-tjunction/line-count"
           (= (cf-line-count "PIPE") 5)
           (strcat "expected 5 (T1 + T1b + 2 T2 pieces + T2b), actual " (itoa (cf-line-count "PIPE"))))

(cf-finish)
