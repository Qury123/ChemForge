;;; Fixture.lsp -- standard drawing used by every pipe-break E2E scenario.
;;;
;;; Layers : INST (instrument line, color 1) / PIPE (pipe, color 3) / OTHER (decoy, color 5)
;;;
;;; Geometry (see README.md for the expected outcome of each crossing):
;;;   L1  INST  horizontal  (0,0)      - (1000,0)
;;;   L2  INST  vertical    (200,-300) - (200,300)     crosses L1, SAME layer   -> must stay whole
;;;   P1  PIPE  vertical    (500,-200) - (500,200)     crosses L1, cross layer  -> L1 gets a gap
;;;   P2  PIPE  vertical    (800,-200) - (800,200)     crosses L1, cross layer  -> L1 gets a gap
;;;   P3  PIPE  horizontal  (600,-100) - (900,-100)    crosses P2, SAME layer   -> must stay whole
;;;   O1  OTHER vertical    (150,-300) - (150,300)     crosses L1 but layer is never selected -> no gap

(defun cf-mklayer (name color)
  (entmake (list (cons 0 "LAYER") (cons 2 name) (cons 70 0) (cons 62 color) (cons 6 "Continuous")))
)

(defun cf-mkline (lay x1 y1 x2 y2)
  (entmake (list (cons 0 "LINE")
                 (cons 8 lay)
                 (cons 10 (list x1 y1 0.0))
                 (cons 11 (list x2 y2 0.0))))
)

(cf-mklayer "INST" 1)
(cf-mklayer "PIPE" 3)
(cf-mklayer "OTHER" 5)

(cf-mkline "INST" 0.0 0.0 1000.0 0.0)
(cf-mkline "INST" 200.0 -300.0 200.0 300.0)
(cf-mkline "PIPE" 500.0 -200.0 500.0 200.0)
(cf-mkline "PIPE" 800.0 -200.0 800.0 200.0)
(cf-mkline "PIPE" 600.0 -100.0 900.0 -100.0)
(cf-mkline "OTHER" 150.0 -300.0 150.0 300.0)

(princ (strcat "\n##CF## fixture ready | INST=" (itoa (cf-line-count "INST"))
               " PIPE=" (itoa (cf-line-count "PIPE"))
               " OTHER=" (itoa (cf-line-count "OTHER"))))
(princ)
