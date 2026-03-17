'use strict';

const express = require('express');
const holdController = require('../controllers/holdController');

const router = express.Router();

router.post('/', holdController.create);
router.get('/:holdId', holdController.getOne);
router.delete('/:holdId', holdController.release);

module.exports = router;
