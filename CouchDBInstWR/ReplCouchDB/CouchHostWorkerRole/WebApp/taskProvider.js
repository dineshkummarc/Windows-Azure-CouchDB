var nano = require('nano');
var tasksDb;

var TaskProvider = function (couchUrl, dbName) {
    var self = this;
    tasksDb = nano(couchUrl).use(dbName);
    self.db = tasksDb;
};

TaskProvider.prototype.findAll = function (callback) {
    //console.log('findAll called');

    var self = this;
    self.db.list({ include_docs: true }, function (error, body) {
        if (error) {
            console.log(error);
            callback(error);
        } else {

            var tasks = [];
            
            for (var i = 0; i < body.rows.length; i++) {
                tasks[i] = body.rows[i].doc;
            }

            console.log('found: ' + JSON.stringify(tasks));
            callback(null, tasks);
        }
    });
};

TaskProvider.prototype.save = function (tasks, callback) {
    //console.log('save called: ' + JSON.stringify(tasks));

    for (var i = 0; i < tasks.length; i++) {
        task = tasks[i];
        task.created_at = new Date();
    }

    var bulkTasks = { "docs": tasks };
    var self = this;
    self.db.bulk(bulkTasks, function (error) {
        if (error) {
            console.log(error);
            callback(error);
        } else {
            //console.log('saved: ' + JSON.stringify(tasks));
            callback(null, tasks);
        }
    });
};

exports.TaskProvider = TaskProvider;
