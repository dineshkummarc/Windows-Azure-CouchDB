module.exports = Home;

function Home (taskProvider) {
  this.taskProvider = taskProvider;
};

Home.prototype = {
  showItems: function (req, res) {
    var self = this;

    this.getItems(function (error, tasklist) {
      if (!tasklist) {
        tasklist = [];
      }

      self.showResults(res, tasklist);
    });
  },

  getItems: function (callback) {
    this.taskProvider.findAll(callback);
  },

  showResults: function (res, tasklist) {
    res.render('home', { pageTitle: 'CouchDB-On-Azure ToDo List', layout: false, tasklist: tasklist });
  },

  newItem: function (req, res) {
    var self = this;

    var createItem = function (resp, tasklist) {
      if (!tasklist) {
        tasklist = [];
      }
      
      var count = tasklist.length;
      var item = req.body.item;
      item.completed = false;

      var newtasks = new Array();
      newtasks[0] = item;
    
      self.taskProvider.save(newtasks,function (error, tasks) {
        self.showItems(req, res);
      });
    };

    this.getItems(createItem);
  },
};
