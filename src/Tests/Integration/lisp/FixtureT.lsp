;;; FixtureT.lsp -- T-junction fixture for the PIPEBREAK endpoint rule.
;;;
;;; Layer : PIPE only (single-layer PIPEBREAK, break direction V -> the vertical line gets cut).
;;;
;;;   T1  PIPE vertical   (500,-200)  - (500,200)   trunk
;;;   T1b PIPE horizontal (499.6,100) - (900,100)   branch whose left end overshoots the trunk
;;;                                                 centreline by 0.4 -- invisible at any zoom,
;;;                                                 i.e. "drawn a hair past the joint".
;;;                                                 -> T1 must stay WHOLE (no gap at all).
;;;   T2  PIPE vertical   (1500,-200) - (1500,200)  trunk with a genuine crossing
;;;   T2b PIPE horizontal (1200,0)    - (1800,0)    runs right through it -> T2 gets a 100 gap.
;;;
;;; T1/T1b is the regression for the old bug: the endpoint test used a 2e-10 tolerance, so a
;;; 0.4 overshoot was read as a real crossing and the trunk was cut at the joint.

(defun cf-mklayer (name color)
  (entmake (list (cons 0 "LAYER") (cons 2 name) (cons 70 0) (cons 62 color) (cons 6 "Continuous")))
)

(defun cf-mkline (lay x1 y1 x2 y2)
  (entmake (list (cons 0 "LINE")
                 (cons 8 lay)
                 (cons 10 (list x1 y1 0.0))
                 (cons 11 (list x2 y2 0.0))))
)

(cf-mklayer "PIPE" 3)

(cf-mkline "PIPE" 500.0 -200.0 500.0 200.0)
(cf-mkline "PIPE" 499.6 100.0 900.0 100.0)
(cf-mkline "PIPE" 1500.0 -200.0 1500.0 200.0)
(cf-mkline "PIPE" 1200.0 0.0 1800.0 0.0)

(princ (strcat "\n##CF## fixtureT ready | PIPE=" (itoa (cf-line-count "PIPE"))))
(princ)
