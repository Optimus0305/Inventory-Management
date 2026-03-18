'use strict';

const express = require('express');
const inventoryController = require('../controllers/inventoryController');

const router = express.Router();

router.get('/', inventoryController.list);
router.get('/:productId', inventoryController.getOne);
router.post('/', inventoryController.create);

module.exports = router;
