;;; Harness.lsp -- shared assertion helpers for the ChemForge pipe-break E2E tests.
;;; Every assertion prints one line prefixed with "##CF## " to stdout so that
;;; Run-IntegrationTests.ps1 can extract and summarise the result.
;;; ASCII-only on purpose: AutoLISP file encoding is not worth the risk here.

(setq CF-PASS 0)
(setq CF-FAIL 0)

(defun cf-report (name ok detail)
  (if ok
    (setq CF-PASS (1+ CF-PASS))
    (setq CF-FAIL (1+ CF-FAIL))
  )
  (princ (strcat "\n##CF## " (if ok "PASS" "FAIL") " | " name " | " detail))
  (princ)
)

(defun cf-finish ()
  (princ (strcat "\n##CF## SUMMARY pass=" (itoa CF-PASS) " fail=" (itoa CF-FAIL)))
  (princ)
)

;; All LINE entities on a layer, as ((start end) ...).
(defun cf-line-pairs (lay / ss i out e)
  (setq out '())
  (setq ss (ssget "X" (list (cons 0 "LINE") (cons 8 lay))))
  (if ss
    (progn
      (setq i 0)
      (while (< i (sslength ss))
        (setq e (entget (ssname ss i)))
        (setq out (cons (list (cdr (assoc 10 e)) (cdr (assoc 11 e))) out))
        (setq i (1+ i))
      )
    )
  )
  out
)

(defun cf-line-count (lay)
  (length (cf-line-pairs lay))
)

(defun cf-approx (a b tol)
  (< (abs (- a b)) tol)
)

;; True when at least one bulge in the list is a half circle (|bulge| = 1).
;; AutoCAD stores a group-42 for every vertex, so a 2-vertex bridge polyline
;; comes back as e.g. (0.0 1.0) -- one flat vertex plus the half-circle one.
(defun cf-has-half-bulge (bulges / r)
  (setq r nil)
  (foreach b bulges
    (if (cf-approx (abs b) 1.0 0.000001) (setq r T))
  )
  r
)

;; Horizontal segments (both endpoints at y0) as [minX maxX], sorted by minX.
(defun cf-hsegs (lay y0 tol / out p1 p2)
  (setq out '())
  (foreach s (cf-line-pairs lay)
    (setq p1 (car s) p2 (cadr s))
    (if (and (cf-approx (cadr p1) y0 tol) (cf-approx (cadr p2) y0 tol))
      (setq out (cons (list (min (car p1) (car p2)) (max (car p1) (car p2))) out))
    )
  )
  (vl-sort out '(lambda (a b) (< (car a) (car b))))
)

;; Vertical segments (both endpoints at x0) as [minY maxY], sorted by minY.
(defun cf-vsegs (lay x0 tol / out p1 p2)
  (setq out '())
  (foreach s (cf-line-pairs lay)
    (setq p1 (car s) p2 (cadr s))
    (if (and (cf-approx (car p1) x0 tol) (cf-approx (car p2) x0 tol))
      (setq out (cons (list (min (cadr p1) (cadr p2)) (max (cadr p1) (cadr p2))) out))
    )
  )
  (vl-sort out '(lambda (a b) (< (car a) (car b))))
)

;; Interval-list equality, item by item, with tolerance.
(defun cf-seqs-equal (a b tol)
  (if (/= (length a) (length b))
    nil
    (if (= (length a) 0)
      T
      (apply 'and
             (mapcar '(lambda (x y)
                        (and (cf-approx (car x) (car y) tol)
                             (cf-approx (cadr x) (cadr y) tol)))
                     a b))
    )
  )
)

(defun cf-fmt-seqs (s / out)
  (setq out "")
  (foreach i s
    (setq out (strcat out "[" (rtos (car i) 2 2) "," (rtos (cadr i) 2 2) "]"))
  )
  out
)

;; Flat number-list equality with tolerance (cf-seqs-equal is for lists of pairs).
(defun cf-nums-equal (a b tol)
  (if (/= (length a) (length b))
    nil
    (apply 'and (mapcar '(lambda (x y) (cf-approx x y tol)) a b))
  )
)

;; LWPOLYLINE inventory on a layer: ((vertexCount (bulge...)) ...).
(defun cf-poly-info (lay / ss i e out n b)
  (setq out '())
  (setq ss (ssget "X" (list (cons 0 "LWPOLYLINE") (cons 8 lay))))
  (if ss
    (progn
      (setq i 0)
      (while (< i (sslength ss))
        (setq e (entget (ssname ss i)))
        (setq n 0)
        (setq b '())
        (foreach x e
          (cond
            ((= (car x) 10) (setq n (1+ n)))
            ((= (car x) 42) (setq b (cons (cdr x) b)))
          )
        )
        (setq out (cons (list n b) out))
        (setq i (1+ i))
      )
    )
  )
  out
)
